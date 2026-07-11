#!/bin/sh

set -u

PROGRAM="cfst"
REPO_OWNER="codingriver"
REPO_NAME="CloudflareSpeedTest-CSharp"
DEFAULT_MANIFEST_URL="https://github.com/${REPO_OWNER}/${REPO_NAME}/releases/latest/download/latest.json"
DEFAULT_INSTALL_DIR="${HOME:-.}/.local/share/${PROGRAM}"
SUPPORTED_TARGETS=" linux-x64 linux-x64-musl linux-arm64 linux-arm64-musl macos-x64 macos-arm64 "
GITHUB_MIRRORS='
https://gh-proxy.303066.xyz
https://gh-proxy.com
https://gh-proxy.org
https://gh-proxy.303066.xyz
https://mirror.ghproxy.com
https://ghfast.top
https://ghp.ci
https://gh.kk.cc
https://gh.aptv.app
https://gh.927223.xyz
https://gh.haonice.com
https://github.akams.cn
https://ui.ghproxy.cc
https://gh.ddc.top
https://gh-proxy.net
https://hub.gitmirror.com
https://github.moeyy.xyz
https://ghfie.geekertao.top
https://proxy.606055.xyz
'

force=0
manifest_url="${CFST_MANIFEST_URL:-$DEFAULT_MANIFEST_URL}"
install_dir="${CFST_HOME:-$DEFAULT_INSTALL_DIR}"
target=""
tmp_dir=""

log() { printf '%s\n' "$*"; }
die() { printf 'Error: %s\n' "$*" >&2; exit 1; }

cleanup() {
    if [ -n "$tmp_dir" ] && [ -d "$tmp_dir" ]; then
        rm -rf "$tmp_dir"
    fi
}
trap cleanup EXIT HUP INT TERM

usage() {
    cat <<EOF
Usage: install.sh [options] [install-directory]

Options:
  --force                 Reinstall the same version and allow downgrade
  --manifest-url <url>    Override manifest URL
  --target <target>       Override detected target
  -h, --help              Show this help

Environment:
  CFST_HOME               Default install directory
  CFST_MANIFEST_URL       Default manifest URL

Supported targets:
  linux-x64 linux-x64-musl linux-arm64 linux-arm64-musl macos-x64 macos-arm64
EOF
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --force)
            force=1
            shift
            ;;
        --manifest-url)
            [ "$#" -ge 2 ] || die "--manifest-url requires a value"
            manifest_url=$2
            shift 2
            ;;
        --target)
            [ "$#" -ge 2 ] || die "--target requires a value"
            target=$2
            shift 2
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        --*)
            die "unknown option: $1"
            ;;
        *)
            [ "$install_dir" = "${CFST_HOME:-$DEFAULT_INSTALL_DIR}" ] || die "multiple install directories are not allowed"
            install_dir=$1
            shift
            if [ "$#" -gt 0 ]; then
                die "multiple install directories are not allowed"
            fi
            ;;
    esac
done

case "$install_dir" in
    "") die "install directory is empty" ;;
esac

has_cmd() { command -v "$1" >/dev/null 2>&1; }

need_cmd() {
    has_cmd "$1" || die "required command not found: $1"
}

github_mirror_url() {
    mirror=$1
    url=$2
    mirror=${mirror%/}

    case "$url" in
        https://github.com/*|https://raw.githubusercontent.com/*|https://objects.githubusercontent.com/*)
            printf '%s/%s\n' "$mirror" "$url"
            ;;
        *)
            return 1
            ;;
    esac
}

is_network_download_error() {
    tool=$1
    status=$2

    case "$tool:$status" in
        curl:5|curl:6|curl:7|curl:28|curl:35|curl:52|curl:56|wget:4)
            return 0
            ;;
        *)
            return 1
            ;;
    esac
}

download_once() {
    url=$1
    out=$2
    tmp_out="$out.tmp.$$"
    rm -f "$tmp_out"

    if has_cmd curl; then
        download_tool=curl
        curl -fsSL --retry 3 --connect-timeout 15 --max-time 300 -o "$tmp_out" "$url"
        download_status=$?
    elif has_cmd wget; then
        download_tool=wget
        wget -q --timeout=300 -O "$tmp_out" "$url"
        download_status=$?
    else
        die "curl or wget is required"
    fi

    if [ "$download_status" -eq 0 ]; then
        if mv "$tmp_out" "$out"; then
            return 0
        else
            download_status=$?
        fi
        rm -f "$tmp_out"
        return "$download_status"
    fi

    rm -f "$tmp_out"
    return "$download_status"
}

download_file() {
    url=$1
    out=$2

    log "Downloading: $url"
    if download_once "$url" "$out"; then
        return 0
    fi

    first_tool=$download_tool
    first_status=$download_status
    if ! is_network_download_error "$first_tool" "$first_status"; then
        return "$first_status"
    fi

    for mirror in $GITHUB_MIRRORS; do
        mirror_url=$(github_mirror_url "$mirror" "$url") || continue
        log "Network error, retrying with GitHub mirror: $mirror"
        if download_once "$mirror_url" "$out"; then
            return 0
        fi
    done

    return "$first_status"
}

detect_musl() {
    if has_cmd ldd && ldd --version 2>&1 | grep -qi 'musl'; then
        return 0
    fi
    if ls /lib/ld-musl-* >/dev/null 2>&1; then
        return 0
    fi
    return 1
}

detect_target() {
    os=$(uname -s 2>/dev/null | tr '[:upper:]' '[:lower:]')
    arch=$(uname -m 2>/dev/null | tr '[:upper:]' '[:lower:]')

    case "$arch" in
        x86_64|amd64) arch="x64" ;;
        aarch64|arm64) arch="arm64" ;;
        *) die "unsupported CPU architecture: $arch" ;;
    esac

    case "$os" in
        linux)
            if detect_musl; then
                printf 'linux-%s-musl\n' "$arch"
            else
                printf 'linux-%s\n' "$arch"
            fi
            ;;
        darwin)
            printf 'macos-%s\n' "$arch"
            ;;
        *)
            die "unsupported operating system: $os"
            ;;
    esac
}

is_supported_target() {
    case "$SUPPORTED_TARGETS" in
        *" $1 "*) return 0 ;;
        *) return 1 ;;
    esac
}

json_asset() {
    manifest=$1
    asset=$2
    if has_cmd jq; then
        jq -r --arg asset "$asset" '
          .version as $version |
          (.assets // [] | map(select(.name == $asset))) as $matches |
          if ($matches | length) != 1 then empty else
            $matches[0] | [$version, .url, (.size|tostring), .sha256] | @tsv
          end
        ' "$manifest"
    elif has_cmd python3; then
        MANIFEST_FILE=$manifest ASSET_NAME=$asset python3 - <<'PY'
import json, os, sys
with open(os.environ["MANIFEST_FILE"], "r", encoding="utf-8") as fh:
    data = json.load(fh)
matches = [a for a in data.get("assets", []) if a.get("name") == os.environ["ASSET_NAME"]]
if len(matches) != 1:
    sys.exit(0)
asset = matches[0]
print("\t".join([str(data.get("version", "")), str(asset.get("url", "")), str(asset.get("size", "")), str(asset.get("sha256", ""))]))
PY
    elif has_cmd node; then
        MANIFEST_FILE=$manifest ASSET_NAME=$asset node <<'JS'
const fs = require('fs');
const data = JSON.parse(fs.readFileSync(process.env.MANIFEST_FILE, 'utf8'));
const matches = (data.assets || []).filter(a => a.name === process.env.ASSET_NAME);
if (matches.length === 1) {
  const asset = matches[0];
  console.log([data.version || '', asset.url || '', String(asset.size || ''), asset.sha256 || ''].join('\t'));
}
JS
    else
        die "jq, python3, or node is required to parse latest.json"
    fi
}

sha256_file() {
    file=$1
    if has_cmd sha256sum; then
        sha256sum "$file" | awk '{print $1}'
    elif has_cmd shasum; then
        shasum -a 256 "$file" | awk '{print $1}'
    elif has_cmd openssl; then
        openssl dgst -sha256 "$file" | awk '{print $NF}'
    else
        die "sha256sum, shasum, or openssl is required"
    fi
}

file_size() {
    file=$1
    wc -c < "$file" | tr -d '[:space:]'
}

version_cmp() {
    a=$(printf '%s' "$1" | sed 's/^v//')
    b=$(printf '%s' "$2" | sed 's/^v//')
    awk -v a="$a" -v b="$b" '
      BEGIN {
        split(a, av, /[.-]/); split(b, bv, /[.-]/);
        for (i = 1; i <= 3; i++) {
          ai = av[i] == "" ? 0 : av[i] + 0;
          bi = bv[i] == "" ? 0 : bv[i] + 0;
          if (ai > bi) { print 1; exit }
          if (ai < bi) { print -1; exit }
        }
        print 0
      }'
}

validate_version() {
    case "$1" in
        ""|*[!0-9A-Za-z.+-]*) return 1 ;;
        *) return 0 ;;
    esac
}

validate_sha256() {
    printf '%s' "$1" | grep -Eq '^[0-9a-f]{64}$'
}

validate_archive_paths() {
    archive=$1
    expected_top=$2
    tar -tzf "$archive" | awk -v top="$expected_top" '
      BEGIN { ok = 1; seen = 0 }
      {
        name = $0;
        if (name == "" || name ~ /^\// || name ~ /(^|\/)\.\.($|\/)/) ok = 0;
        if (index(name, top "/") != 1 && name != top) ok = 0;
        seen = 1;
      }
      END { exit (ok && seen) ? 0 : 1 }'
}

write_launcher() {
    launcher=$1
    tmp_launcher="$launcher.$$"
    cat > "$tmp_launcher" <<'EOF'
#!/bin/sh
set -eu

CFST_HOME=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
CURRENT=$(cat "$CFST_HOME/.current-release")

case "$CURRENT" in
    ""|*/*|*".."*)
        echo "cfst: invalid current release" >&2
        exit 1
        ;;
esac

cd "$CFST_HOME"
exec "$CFST_HOME/releases/$CURRENT/cfst" "$@"
EOF
    chmod +x "$tmp_launcher"
    mv "$tmp_launcher" "$launcher"
}

copy_default_config() {
    src_dir=$1
    dst_dir=$2
    [ -d "$src_dir" ] || return 0
    find "$src_dir" -type f | while IFS= read -r src; do
        rel=${src#"$src_dir"/}
        dst="$dst_dir/$rel"
        if [ ! -e "$dst" ]; then
            parent=$(dirname "$dst")
            mkdir -p "$parent"
            cp "$src" "$dst"
        fi
    done
}

need_cmd uname
need_cmd tar
need_cmd awk
need_cmd sed
need_cmd grep
need_cmd wc
need_cmd mkdir
need_cmd mv
need_cmd chmod

if [ -z "$target" ]; then
    target=$(detect_target)
fi
is_supported_target "$target" || die "unsupported target: $target"

asset_name="${PROGRAM}-${target}.tar.gz"
mkdir -p "$install_dir" || die "failed to create install directory: $install_dir"
install_dir=$(CDPATH= cd -- "$install_dir" && pwd)
tmp_dir=$(mktemp -d "$install_dir/.install-tmp.XXXXXX") || die "failed to create temporary directory"

manifest_file="$tmp_dir/latest.json"
archive_file="$tmp_dir/$asset_name"

log "cfst installer"
log "  install dir: $install_dir"
log "  target:      $target"
log "  manifest:    $manifest_url"

download_file "$manifest_url" "$manifest_file" || die "failed to download manifest"

asset_line=$(json_asset "$manifest_file" "$asset_name")
[ -n "$asset_line" ] || die "manifest does not contain exactly one asset named $asset_name"

version=$(printf '%s\n' "$asset_line" | awk -F '\t' '{print $1}')
asset_url=$(printf '%s\n' "$asset_line" | awk -F '\t' '{print $2}')
expected_size=$(printf '%s\n' "$asset_line" | awk -F '\t' '{print $3}')
expected_sha=$(printf '%s\n' "$asset_line" | awk -F '\t' '{print $4}')

validate_version "$version" || die "invalid manifest version: $version"
case "$asset_url" in http://*|https://*) ;; *) die "invalid asset URL: $asset_url" ;; esac
case "$expected_size" in ''|*[!0-9]*) die "invalid asset size: $expected_size" ;; esac
validate_sha256 "$expected_sha" || die "invalid asset sha256: $expected_sha"

local_version=""
if [ -f "$install_dir/.installed-version" ]; then
    local_version=$(tr -d '[:space:]' < "$install_dir/.installed-version")
fi

log "  local:       ${local_version:-none}"
log "  remote:      $version"

if [ -n "$local_version" ] && [ "$force" -ne 1 ]; then
    cmp=$(version_cmp "$local_version" "$version")
    if [ "$cmp" -eq 0 ] && [ -f "$install_dir/.current-release" ] && [ -x "$install_dir/$PROGRAM" ]; then
        log "Already up to date."
        exit 0
    fi
    if [ "$cmp" -gt 0 ]; then
        log "Local version is newer than remote; skipped. Use --force to downgrade."
        exit 0
    fi
fi

log "Downloading $asset_name..."
download_file "$asset_url" "$archive_file" || die "failed to download asset"

actual_size=$(file_size "$archive_file")
[ "$actual_size" = "$expected_size" ] || die "size mismatch: expected $expected_size, got $actual_size"

actual_sha=$(sha256_file "$archive_file")
[ "$actual_sha" = "$expected_sha" ] || die "sha256 mismatch: expected $expected_sha, got $actual_sha"

top_dir="${PROGRAM}-${target}"
validate_archive_paths "$archive_file" "$top_dir" || die "archive contains invalid paths"

extract_dir="$tmp_dir/extract"
mkdir -p "$extract_dir"
tar -xzf "$archive_file" -C "$extract_dir" || die "failed to extract archive"
package_dir="$extract_dir/$top_dir"

[ -d "$package_dir" ] || die "package top directory missing: $top_dir"
[ -f "$package_dir/VERSION" ] || die "package VERSION missing"
[ "$(tr -d '[:space:]' < "$package_dir/VERSION")" = "$version" ] || die "package VERSION does not match manifest"
[ -f "$package_dir/$PROGRAM" ] || die "package executable missing: $PROGRAM"
chmod +x "$package_dir/$PROGRAM"
[ -x "$package_dir/$PROGRAM" ] || die "package executable is not executable"

program_version=$("$package_dir/$PROGRAM" --version 2>/dev/null || true)
program_version=$(printf '%s' "$program_version" | tr -d '[:space:]')
[ "$program_version" = "$version" ] || die "program --version ($program_version) does not match manifest ($version)"

sha_prefix=$(printf '%s' "$expected_sha" | cut -c 1-8)
release_name="${version}-${target}-${sha_prefix}"
release_parent="$install_dir/releases"
release_dir="$release_parent/$release_name"

mkdir -p "$release_parent" "$install_dir/config" "$install_dir/data" "$install_dir/res" "$install_dir/publish"
copy_default_config "$package_dir/config" "$install_dir/config"

if [ -e "$release_dir" ]; then
    rm -rf "$release_dir"
fi
mv "$package_dir" "$release_dir" || die "failed to install release directory"

printf '%s\n' "$release_name" > "$install_dir/.current-release.$$"
mv "$install_dir/.current-release.$$" "$install_dir/.current-release"

printf '%s\n' "$version" > "$install_dir/.installed-version.$$"
mv "$install_dir/.installed-version.$$" "$install_dir/.installed-version"

write_launcher "$install_dir/$PROGRAM"

log "Installed cfst $version."
log "Run: $install_dir/$PROGRAM"
