#!/usr/bin/env bash
# CloudflareST multi-platform build script
# Usage: ./build.sh [platform] [-fd]
#        ./build.sh -clean    # 清理 obj、bin、publish 等缓存目录
# Example: ./build.sh         # build all (self-contained)
#          ./build.sh -fd     # build all (framework-dependent, ~几百 KB)

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR"
PUBLISH_BASE="$PROJECT_ROOT/publish"

fd=false
platform="all"
for arg in "$@"; do
    if [[ "$arg" == "-fd" ]]; then fd=true
    elif [[ "$arg" == "-clean" ]]; then
        for d in obj bin publish; do
            if [[ -d "$PROJECT_ROOT/$d" ]]; then
                rm -rf "$PROJECT_ROOT/$d"
                echo "已清理: $PROJECT_ROOT/$d"
            fi
        done
        echo "清理完成。"
        exit 0
    elif [[ "$arg" == "all" || "$arg" == "win-x64" || "$arg" == "linux-x64" || "$arg" == "osx-x64" || "$arg" == "osx-arm64" ]]; then
        platform="$arg"
    fi
done

publish_rid() {
    local rid="$1"
    local desc="$2"
    local exe="$3"
    local suffix=""
    [[ "$fd" == true ]] && suffix="-fd"
    local out_dir="$PUBLISH_BASE/${rid}${suffix}"
    local mode="自包含"
    [[ "$fd" == true ]] && mode="fd (需 .NET 8)"
    echo ""
    echo "==> $desc ($rid) [$mode]"
    local self_contained="true"
    [[ "$fd" == true ]] && self_contained="false"
    local compression="true"
    local trimmed="true"
    [[ "$fd" == true ]] && compression="false" && trimmed="false"
    dotnet publish -r "$rid" -c Release --self-contained "$self_contained" \
        -p:PublishSingleFile=true \
        -p:PublishTrimmed=$trimmed \
        -p:EnableCompressionInSingleFile=$compression \
        -o "$out_dir" \
        "$PROJECT_ROOT"
    local exe_path="$out_dir/$exe"
    if [[ -f "$exe_path" ]]; then
        # 本地打包输出文件名与 GitHub Actions Release 保持一致，且不再放到子文件夹中
        # 如：cfst-win-x64 / cfst-win-x64.exe / cfst-linux-x64 等
        local final_name
        if [[ "$rid" == "win-x64" ]]; then
            final_name="cfst-${rid}${suffix}.exe"
        else
            final_name="cfst-${rid}${suffix}"
        fi
        local final_path="$PUBLISH_BASE/$final_name"
        cp "$exe_path" "$final_path"

        size=$(wc -c < "$final_path" 2>/dev/null || echo 0)
        size_mb=$(awk "BEGIN { printf \"%.2f\", $size / 1048576 }" 2>/dev/null || echo "?")
        echo "    Output: $final_path (${size_mb} MB)"
    fi
}

cd "$PROJECT_ROOT"
mode="自包含"
[[ "$fd" == true ]] && mode="fd (需 .NET 8)"
echo "CloudflareST build - Project: $PROJECT_ROOT [$mode]"

case "$platform" in
    all)
        publish_rid "win-x64"   "Windows x64"              "cfst.exe"
        publish_rid "linux-x64" "Linux x64"                 "cfst"
        publish_rid "osx-x64"   "macOS Intel (x64)"         "cfst"
        publish_rid "osx-arm64" "macOS Apple Silicon"       "cfst"
        ;;
    win-x64|linux-x64|osx-x64|osx-arm64)
        case "$platform" in
            win-x64)   publish_rid "win-x64"   "Windows x64"        "cfst.exe" ;;
            linux-x64) publish_rid "linux-x64" "Linux x64"           "cfst" ;;
            osx-x64)   publish_rid "osx-x64"   "macOS Intel"        "cfst" ;;
            osx-arm64) publish_rid "osx-arm64" "macOS Apple Silicon" "cfst" ;;
        esac
        ;;
    *)
        echo "Usage: $0 [all|win-x64|linux-x64|osx-x64|osx-arm64] [-fd]"
        exit 1
        ;;
esac

echo ""
echo "Done. Output: $PUBLISH_BASE"
