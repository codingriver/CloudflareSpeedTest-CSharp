#!/bin/bash
set -e

export PATH="/usr/local/bin:/usr/bin:/bin:$PATH"

# ===================== 配置区 =====================
REPO_OWNER="codingriver"
REPO_NAME="CloudflareSpeedTest-CSharp"
GITHUB_API_URL="https://api.github.com/repos/${REPO_OWNER}/${REPO_NAME}/releases/latest"

CDN_MIRRORS=(
 "https://ghproxy.com/https://github.com"
 "https://gh-proxy.com/https://github.com"
 "https://mirror.ghproxy.com/https://github.com"
 "https://github.moeyy.xyz/https://github.com"
)

DNS_API_URL="http://127.0.0.1:58080/api/dns.php"
IP_FILE="./onlyip.txt"

# 工作目录自动定位到脚本所在目录（计划任务执行时即为 data/tasks/）
cd "$(cd "$(dirname "$0")" && pwd)"

# ===================== 函数区 =====================

# 检测当前系统是否使用 musl libc
is_musl() {
 # 优先确认 glibc — 大多数 Linux 都是 glibc
 if ldd --version 2>&1 | grep -qiE 'gnu libc|glibc'; then
 return 1
 fi
 # 明确检测到 musl 动态链接器
 if ls /lib/ld-musl-* &>/dev/null; then
 return 0
 fi
 # ldd 版本明确提到 musl
 if ldd --version 2>&1 | grep -qi musl; then
 return 0
 fi
 # 默认保守假设为 glibc（避免误判）
 return 1
}

get_cfst_filename() {
 local os arch libc_suffix
 os=$(uname -s | tr '[:upper:]' '[:lower:]')
 arch=$(uname -m)

 # 判断 libc 类型（仅限 Linux）
 libc_suffix=""
 if [ "$os" = "linux" ]; then
 if is_musl; then
 libc_suffix="-musl"
 else
 libc_suffix="-glibc"
 fi
 fi

 case "$os" in
 linux)
 case "$arch" in
 x86_64|amd64) echo "cfst-linux${libc_suffix}-x64-upx" ;;
 aarch64|arm64|armv8l) echo "cfst-linux${libc_suffix}-arm64-upx" ;;
 armv7l|armv6l) echo "cfst-linux${libc_suffix}-arm64-upx" ;;
 *) echo "不支持架构: $arch" >&2; exit 1 ;;
 esac
 ;;
 darwin)
 case "$arch" in
 x86_64|amd64) echo "cfst-macos-x64" ;;
 aarch64|arm64) echo "cfst-macos-arm64" ;;
 *) echo "不支持架构: $arch" >&2; exit 1 ;;
 esac
 ;;
 *) echo "不支持系统: $os" >&2; exit 1 ;;
 esac
}

# 获取 fallback 文件名（旧版命名，用于兼容旧 Release）
get_cfst_filename_fallback() {
 local os arch
 os=$(uname -s | tr '[:upper:]' '[:lower:]')
 arch=$(uname -m)

 case "$os" in
 linux)
 case "$arch" in
 x86_64|amd64) echo "cfst-linux-x64-upx" ;;
 aarch64|arm64|armv8l) echo "cfst-linux-arm64-upx" ;;
 armv7l|armv6l) echo "cfst-linux-arm64-upx" ;;
 *) echo "不支持架构: $arch" >&2; exit 1 ;;
 esac
 ;;
 darwin)
 case "$arch" in
 x86_64|amd64) echo "cfst-macos-x64" ;;
 aarch64|arm64) echo "cfst-macos-arm64" ;;
 *) echo "不支持架构: $arch" >&2; exit 1 ;;
 esac
 ;;
 *) echo "不支持系统: $os" >&2; exit 1 ;;
 esac
}

get_latest_version() {
 local resp
 resp=$(curl -fsSL --max-time 15 "$GITHUB_API_URL" 2>/dev/null) || { echo ""; return; }
 echo "$resp" | grep -o '"tag_name": *"[^"]*"' | head -1 | sed 's/.*"\([^"]*\)".*/\1/'
}

get_local_version() {
 if [ -f ".cfst.version" ]; then
 cat ".cfst.version" | tr -d '[:space:]'
 else
 echo ""
 fi
}

save_local_version() { echo "$1" > ".cfst.version"; }

download_file() {
 local url="$1" out="$2" retry=0
 while [ $retry -lt 3 ]; do
 echo " 下载: $url"
 if curl -fsSL --max-time 60 -o "$out.tmp" "$url" 2>/dev/null; then
 mv "$out.tmp" "$out"
 return 0
 fi
 retry=$((retry + 1))
 echo " 失败，重试 ($retry/3)..."
 sleep 2
 done
 return 1
}

download_cfst() {
 local filename="$1" version="$2"
 local base_url="https://github.com/${REPO_OWNER}/${REPO_NAME}/releases/download/${version}"
 local ok=0

 echo "正在下载 ${filename} (版本 ${version})..."

 if download_file "${base_url}/${filename}" "$filename"; then
 echo " ✓ GitHub 主站下载成功"
 ok=1
 else
 echo " ✗ GitHub 失败，尝试 CDN..."
 for mirror in "${CDN_MIRRORS[@]}"; do
 if download_file "${mirror}/${REPO_OWNER}/${REPO_NAME}/releases/download/${version}/${filename}" "$filename"; then
 echo " ✓ CDN 成功: $mirror"
 ok=1
 break
 fi
 done
 fi

 if [ $ok -eq 0 ]; then
 echo "错误：所有下载源均失败" >&2
 rm -f "$filename.tmp"
 return 1
 fi

 # 校验文件是否为有效 ELF 可执行文件（防止 CDN 返回 HTML 错误页面）
 if ! file "$filename" | grep -qiE 'elf|executable|mach-o'; then
 echo "错误：下载的文件不是有效的可执行文件（可能 CDN 返回了错误页面）" >&2
 rm -f "$filename"
 return 1
 fi

 chmod +x "$filename"
 echo " ✓ 已设置可执行权限"
 return 0
}

check_and_update_cfst() {
 local filename latest localv need=0
 local fallback_filename=""
 filename=$(get_cfst_filename)
 fallback_filename=$(get_cfst_filename_fallback)
 echo "当前平台文件: $filename"

 echo "检查最新版本..."
 latest=$(get_latest_version)
 if [ -z "$latest" ]; then
 echo " 警告：无法获取 GitHub 版本，将使用本地文件"
 latest="unknown"
 else
 echo " 最新版本: $latest"
 fi

 localv=$(get_local_version)
 echo " 本地版本: ${localv:-未记录}"

 if [ ! -f "$filename" ] && [ ! -f "$fallback_filename" ]; then
 echo " 本地文件不存在，需要下载"
 need=1
 elif [ -f "$filename" ] && [ ! -x "$filename" ]; then
 echo " 文件不可执行，需要重新下载"
 need=1
 elif [ -f "$fallback_filename" ] && [ ! -x "$fallback_filename" ]; then
 echo " 文件不可执行，需要重新下载"
 need=1
 elif [ -n "$localv" ] && [ "$localv" != "$latest" ] && [ "$latest" != "unknown" ]; then
 echo " 发现新版本 ($latest)"
 need=1
 elif [ -z "$localv" ]; then
 echo " 本地版本未记录，将重新下载"
 need=1
 else
 echo " ✓ 本地文件已是最新，跳过下载"
 fi

 if [ $need -eq 1 ]; then
 if [ "$latest" = "unknown" ]; then
 # 检查本地是否有任一版本可用
 if [ -f "$filename" ] || [ -f "$fallback_filename" ]; then
 echo " 无法获取远程版本，使用本地文件"
 else
 echo "错误：无法获取版本且本地文件不存在" >&2
 exit 1
 fi
 else
 # 先尝试新版名称下载
 local downloaded=0
 if download_cfst "$filename" "$latest"; then
 downloaded=1
 # 清理可能存在的旧版 fallback 文件
 rm -f "$fallback_filename"
 else
 echo " 新版产物不存在，尝试旧版命名..."
 if download_cfst "$fallback_filename" "$latest"; then
 downloaded=1
 fi
 fi

 if [ $downloaded -eq 1 ]; then
 save_local_version "$latest"
 echo " ✓ 更新完成"
 else
 echo "错误：下载失败" >&2
 exit 1
 fi
 fi
 fi
 echo ""
}

run_cfst() {
 local filename="$1"
 echo "开始运行 CloudflareSpeedTest..."
 "./$filename" -tcping -silent -onlyip onlyip.txt -p 20 -dn 20
 echo "测速完成"
 echo ""
}

process_and_update_dns() {
 local file="$IP_FILE" line_num=0
 echo "处理 IP 并更新 DNS..."
 if [ ! -f "$file" ]; then
 echo "错误：$file 不存在" >&2
 exit 1
 fi

 while IFS= read -r line; do
 line_num=$((line_num + 1))
 [ -z "$line" ] && continue
 if echo "$line" | grep -qE '^([0-9]{1,3}\.){3}[0-9]{1,3}$'; then
 local valid=true o1 o2 o3 o4
 IFS='.' read -r o1 o2 o3 o4 <<< "$line"
 for octet in $o1 $o2 $o3 $o4; do
 if [ "$octet" -lt 0 ] || [ "$octet" -gt 255 ]; then
 valid=false
 break
 fi
 done
 if [ "$valid" = true ]; then
 local domain="${DNS_DOMAIN_PREFIX}${line_num}.${DNS_DOMAIN_SUFFIX}"
 echo "第 ${line_num} 行: $line -> $domain"
 local resp
 resp=$(curl -fsS --max-time 30 "${DNS_API_URL}?action=update&domain=${domain}&value=${line}" 2>&1) || {
 echo " 错误：API 失败 - $resp" >&2
 continue
 }
 echo " 响应: $resp"
 else
 echo "警告：第 ${line_num} 行 IP 数值超出范围: $line" >&2
 fi
 else
 echo "警告：第 ${line_num} 行不是有效 IPv4: $line" >&2
 fi
 done < "$file"
 echo "处理完成，共 $line_num 行"
}

# ===================== 主程序 =====================
echo "======================== start ========================"
echo "工作目录: $(pwd)"
echo ""

check_and_update_cfst
CFST_FILE=$(get_cfst_filename)
# 如果新版名称不存在但旧版 fallback 存在，使用旧版
if [ ! -f "$CFST_FILE" ]; then
 local fb
 fb=$(get_cfst_filename_fallback)
 if [ -f "$fb" ]; then
 CFST_FILE="$fb"
 fi
fi
run_cfst "$CFST_FILE"
process_and_update_dns

echo "======================== end ========================"
