#!/usr/bin/env bash
# CloudflareST multi-platform build script
# Usage: ./build.sh [platform] [mode] [-upx] [-clean]
#
# === 构建模式 ===
#
# (默认)  自包含单文件
#         --self-contained true  -p:PublishSingleFile=true
#         -p:PublishTrimmed=true -p:EnableCompressionInSingleFile=true
#         裁剪(Trim): YES  内置压缩: YES  需要运行时: NO   体积: ~25 MB
#
# -fd     框架依赖单文件
#         --self-contained false -p:PublishSingleFile=true
#         裁剪(Trim): NO   内置压缩: NO   需要运行时: YES(.NET 8)  体积: ~几百 KB
#
# -aot    NativeAOT 原生编译
#         -p:PublishAot=true -p:StripSymbols=true
#         裁剪(Trim): YES(强制) 内置压缩: NO  需要运行时: NO  体积: ~10 MB
#         前置依赖:
#           Linux   : clang, zlib1g-dev  (sudo apt install clang zlib1g-dev)
#           macOS   : Xcode Command Line Tools (xcode-select --install)
#         注意: AOT 不支持部分反射/动态代码，构建时会有警告提示不兼容项
#
# === UPX 压缩（可叠加到任意模式）===
#
# -upx    使用 UPX 对输出文件再次压缩（需已安装 UPX 并在 PATH 中）
#         效果: 在内置压缩基础上再缩小约 30~50%
#         默认(自包含)+upx : ~25 MB -> ~12 MB
#         aot+upx          : ~10 MB -> ~5 MB
#         fd+upx           : 效果有限，不推荐
#         注意: UPX 压缩的文件部分杀软可能误报，面向用户发布时请酌情使用
#
#         UPX 安装方式:
#           macOS   : brew install upx
#           Linux   : sudo apt install upx        # Debian/Ubuntu
#                     sudo yum install upx        # CentOS/RHEL
#                     sudo pacman -S upx          # Arch
#                     sudo apk add upx            # Alpine
#
# === 清理 ===
#
# -clean  清理 obj/bin/publish 目录（可与其他参数组合）
#
# === 示例 ===
# ./build.sh                        # 全平台 自包含+Trim+内置压缩  ~25 MB
# ./build.sh win-x64                # 仅 win-x64 自包含
# ./build.sh -fd                    # 全平台 框架依赖  ~几百 KB
# ./build.sh win-x64 -fd            # 仅 win-x64 框架依赖
# ./build.sh -aot                   # 全平台 NativeAOT  ~10 MB
# ./build.sh linux-x64 -aot         # 仅 linux-x64 NativeAOT
# ./build.sh -upx                   # 全平台 自包含 + UPX 二次压缩  ~12 MB
# ./build.sh linux-x64 -aot -upx    # linux-x64 NativeAOT + UPX  ~5 MB（最小）
# ./build.sh -clean                 # 仅清理
# ./build.sh win-x64 -clean         # 清理后构建 win-x64

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PUBLISH_BASE="$PROJECT_ROOT/publish"

fd=false
aot=false
upx=false
clean=false
platform="all"
platform_explicit=false

for arg in "$@"; do
    if [[ "$arg" == "-fd" ]];    then fd=true
    elif [[ "$arg" == "-aot" ]]; then aot=true
    elif [[ "$arg" == "-upx" ]]; then upx=true
    elif [[ "$arg" == "-clean" ]]; then clean=true
    elif [[ "$arg" == "all" || "$arg" == "win-x64" || "$arg" == "linux-x64" || "$arg" == "osx-x64" || "$arg" == "osx-arm64" ]]; then
        platform="$arg"
        platform_explicit=true
    fi
done

publish_rid() {
    local rid="$1"
    local desc="$2"
    local exe="$3"
    local suffix=""
    [[ "$fd"  == true ]] && suffix="-fd"
    [[ "$aot" == true ]] && suffix="-aot"
    local out_dir="$PUBLISH_BASE/${rid}${suffix}"
    local mode="自包含"
    [[ "$fd"  == true ]] && mode="fd (需 .NET 8)"
    [[ "$aot" == true ]] && mode="NativeAOT"
    local upx_note=""
    [[ "$upx" == true ]] && upx_note="+UPX"
    echo ""
    echo "==> $desc ($rid) [$mode$upx_note]"

    if [[ "$aot" == true ]]; then
        dotnet publish -r "$rid" -c Release \
            -p:PublishAot=true \
            -p:StripSymbols=true \
            -o "$out_dir" \
            "$PROJECT_ROOT"
    elif [[ "$fd" == true ]]; then
        dotnet publish -r "$rid" -c Release --self-contained false \
            -p:PublishSingleFile=true \
            -p:PublishTrimmed=false \
            -p:EnableCompressionInSingleFile=false \
            -o "$out_dir" \
            "$PROJECT_ROOT"
    else
        dotnet publish -r "$rid" -c Release --self-contained true \
            -p:PublishSingleFile=true \
            -p:PublishTrimmed=true \
            -p:EnableCompressionInSingleFile=true \
            -o "$out_dir" \
            "$PROJECT_ROOT"
    fi

    local exe_path="$out_dir/$exe"
    [[ ! -f "$exe_path" ]] && return

    # UPX 压缩
    if [[ "$upx" == true ]]; then
        if command -v upx &>/dev/null; then
            echo "    UPX compressing..."
            upx --best --lzma "$exe_path" 2>&1 | tail -1
        else
            echo "    [警告] 未找到 upx，跳过 UPX 压缩。请安装: brew install upx 或 sudo apt install upx"
        fi
    fi

    local final_name
    if [[ "$rid" == "win-x64" ]]; then
        final_name="cfst-${rid}${suffix}.exe"
    else
        final_name="cfst-${rid}${suffix}"
    fi
    local final_path="$PUBLISH_BASE/$final_name"
    cp "$exe_path" "$final_path"
    local size size_mb
    size=$(wc -c < "$final_path" 2>/dev/null || echo 0)
    size_mb=$(awk "BEGIN { printf \"%.2f\", $size / 1048576 }" 2>/dev/null || echo "?")
    echo "    Output: $final_path (${size_mb} MB)"
}

cd "$PROJECT_ROOT"

# -clean：清理 obj / bin / publish
if [[ "$clean" == true ]]; then
    echo "Cleaning obj/bin/publish..."
    rm -rf "$PROJECT_ROOT/obj" "$PROJECT_ROOT/bin" "$PUBLISH_BASE"
    echo "Clean done."
    if [[ "$platform_explicit" == false ]]; then
        exit 0
    fi
fi

mode="自包含"
[[ "$fd"  == true ]] && mode="fd (需 .NET 8)"
[[ "$aot" == true ]] && mode="NativeAOT"
upx_note=""
[[ "$upx" == true ]] && upx_note=" +UPX"
echo "CloudflareST build - Project: $PROJECT_ROOT [$mode$upx_note]"

case "$platform" in
    all)
        publish_rid "win-x64"   "Windows x64"         "cfst.exe"
        publish_rid "linux-x64" "Linux x64"            "cfst"
        publish_rid "osx-x64"   "macOS Intel (x64)"    "cfst"
        publish_rid "osx-arm64" "macOS Apple Silicon"  "cfst"
        ;;
    win-x64|linux-x64|osx-x64|osx-arm64)
        case "$platform" in
            win-x64)   publish_rid "win-x64"   "Windows x64"        "cfst.exe" ;;
            linux-x64) publish_rid "linux-x64" "Linux x64"           "cfst" ;;
            osx-x64)   publish_rid "osx-x64"   "macOS Intel"         "cfst" ;;
            osx-arm64) publish_rid "osx-arm64" "macOS Apple Silicon" "cfst" ;;
        esac
        ;;
    *)
        echo "Usage: $0 [all|win-x64|linux-x64|osx-x64|osx-arm64] [-fd] [-aot] [-upx] [-clean]"
        exit 1
        ;;
esac

echo ""
echo "Done. Output: $PUBLISH_BASE"
