#!/usr/bin/env bash
# CloudflareST multi-platform build script
# Usage: ./build.sh [platform] [-fd]
# Example: ./build.sh         # build all (self-contained)
#          ./build.sh -fd     # build all (framework-dependent, ~几百 KB)

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PUBLISH_BASE="$PROJECT_ROOT/publish"

fd=false
platform="all"
for arg in "$@"; do
    if [[ "$arg" == "-fd" ]]; then fd=true
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
        size=$(wc -c < "$exe_path" 2>/dev/null || echo 0)
        size_mb=$(awk "BEGIN { printf \"%.2f\", $size / 1048576 }" 2>/dev/null || echo "?")
        echo "    Output: $out_dir/$exe (${size_mb} MB)"
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
