# Cloudflare Speed Test - C# 版

基于 [XIU2/CloudflareSpeedTest](https://github.com/XIU2/CloudflareSpeedTest) 的 C# 实现，用于从 Cloudflare CDN IP 中筛选延迟低、速度快的节点。

## 功能

- **ICMP Ping 延迟测试**（默认）：使用 `System.Net.NetworkInformation.Ping`，简单快捷
- **TCPing 延迟测试**：`-tcping` 时使用，并发测速，串行多次取平均
- **HTTPing 延迟测试**：`-httping` 时使用，HTTP HEAD 测应用层延迟，支持地区码解析与过滤
- **多 CDN 地区码**：支持 Cloudflare、AWS、Fastly、CDN77、Bunny、Gcore
- **HTTP 下载测速**：通过 ConnectCallback 绑定到待测 IP
- **IP 列表**：本地 ip.txt/ipv6.txt，缺失时从 [CloudflareIP-Sync](https://github.com/codingriver/CloudflareIP-Sync) 经 jsDelivr CDN 自动下载
- **CSV 导出**：含地区码、地区中文列

## 环境

- .NET 8.0 运行时（或使用单文件发布的可执行文件，无需安装）
- Windows / Linux / macOS

## 编译

```bash
dotnet build
```

## 快速开始

```bash
# 默认运行（ICMP 延迟 + 下载测速）
./cfst

# Windows
.\cfst.exe
```

首次运行若本地无 `ip.txt`，会自动从 CDN 下载 Cloudflare IP 段。

## 常用示例

### 仅测延迟（不测下载速度）

```bash
./cfst -dd
./cfst -tcping -dd
./cfst -httping -dd
```

### 指定下载测速 URL

```bash
# HTTP 测速（端口 80）
./cfst -url "http://speedtest.303066.xyz/__down?bytes=104857600" -tp 80

# Cloudflare 官方测速（HTTPS）
./cfst -url "https://speed.cloudflare.com/__down?bytes=52428800"
```

### 限制加载 IP 数量

```bash
./cfst -ipn 2000
./cfst -ipn 500 -dd
```

### 指定 IP 段

```bash
./cfst -ip "173.245.48.0/20,104.16.0.0/13"
./cfst -f my-ip.txt
```

### 延迟测速模式

```bash
./cfst              # ICMP Ping（默认）
./cfst -tcping      # TCPing（推荐，无需 ICMP 权限）
./cfst -httping -url "https://cf.xiu2.xyz/url"  # HTTPing + 地区码
```

### 地区码过滤（仅 HTTPing）

```bash
./cfst -httping -cfcolo "HKG,NRT,LAX"
```

### 全量 IP 与并发调整

```bash
./cfst -allip
./cfst -n 500 -dn 20 -t 8
```

### 静默模式（仅输出 IP）

```bash
# 只输出 IP 地址，每行一个，无其他输出；出错或 0 结果时输出空并写 onlyip.txt
./cfst -silent
./cfst -q
```

适用于脚本调用、管道等场景。

## 参数一览

| 参数 | 默认值 | 说明 |
|------|--------|------|
| **IP 来源** | | |
| `-f` | ip.txt | IPv4 段文件 |
| `-f6` | ipv6.txt | IPv6 段文件 |
| `-ip` | - | 直接指定 CIDR，逗号分隔 |
| `-ipn` | 0 | 加载 IP 数量上限，0=不限制 |
| **延迟测速** | | |
| `-n` | 200 | 延迟测速并发数 |
| `-t` | 4 | 单 IP 测速次数 |
| `-tl` | 9999 | 延迟上限(ms) |
| `-tll` | 0 | 延迟下限(ms) |
| `-tlr` | 1.0 | 丢包率上限 |
| `-tcping` | false | 使用 TCPing（默认 ICMP） |
| `-httping` | false | 使用 HTTPing |
| `-httping-code` | 0 | 有效状态码，0=200/301/302 |
| `-cfcolo` | - | 地区码过滤（仅 HTTPing） |
| **下载测速** | | |
| `-url` | 见 Config | 测速下载地址 |
| `-tp` | 443 | 测速端口（HTTP 用 80） |
| `-dn` | 10 | 参与下载测速的 IP 数 |
| `-dt` | 10 | 下载测速超时(秒) |
| `-sl` | 0 | 速度下限(MB/s)，低于则过滤 |
| `-dd` | false | 禁用下载测速 |
| **输出** | | |
| `-o` | result.csv | 输出 CSV 文件 |
| `-p` | 10 | 控制台显示条数 |
| **其他** | | |
| `-allip` | false | 全量 IP（默认每/24 随机一个） |
| `-debug` | false | 调试输出 |
| `-silent` / `-q` | false | 静默模式：仅输出 IP（每行一个），无其他输出；出错或 0 结果时输出空并写 onlyip.txt |
| `-onlyip` | onlyip.txt | 静默模式下的 IP 输出文件 |

## 单文件发布

### 一键打包（推荐）

```powershell
# Windows PowerShell
.\scripts\build.ps1
.\scripts\build.ps1 win-x64
.\scripts\build.ps1 -fd           # 依赖框架版（需 .NET 8，体积 ~几百 KB）
.\scripts\build.ps1 win-x64 -fd
```

```bash
# Linux / macOS / Git Bash
./scripts/build.sh
./scripts/build.sh win-x64
./scripts/build.sh -fd             # 依赖框架版（需 .NET 8，体积 ~几百 KB）
./scripts/build.sh win-x64 -fd
```

### 手动打包

```bash
# 自包含（默认）
dotnet publish -r win-x64 -c Release --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -p:EnableCompressionInSingleFile=true -o publish/win-x64
dotnet publish -r linux-x64 -c Release --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -p:EnableCompressionInSingleFile=true -o publish/linux-x64
dotnet publish -r osx-x64 -c Release --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -p:EnableCompressionInSingleFile=true -o publish/osx-x64
dotnet publish -r osx-arm64 -c Release --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -p:EnableCompressionInSingleFile=true -o publish/osx-arm64

# 依赖框架（需安装 .NET 8，体积小，不支持 Trimmed/Compression）
dotnet publish -r win-x64 -c Release --self-contained false -p:PublishSingleFile=true -p:PublishTrimmed=false -o publish/win-x64-fd
```

输出在 `publish/<rid>/` 或 `publish/<rid>-fd/` 目录。

## 项目结构

```
CloudflareSeedTest-CSharp/
├── CloudflareST.csproj
├── Program.cs        # 主程序入口
├── Config.cs         # 配置
├── IpProvider.cs     # IP 列表加载
├── IcmpPinger.cs    # ICMP Ping 延迟测试（默认）
├── PingTester.cs    # TCPing 延迟测试
├── HttpingTester.cs # HTTPing 延迟测试
├── ColoProvider.cs  # CDN 地区码解析
├── SpeedTester.cs   # HTTP 下载测速
├── OutputWriter.cs  # 结果输出
├── Models/IPInfo.cs # 数据结构
├── scripts/        # 打包脚本
└── docs/           # 使用文档
```

## 输出说明

### 控制台表格

```
序号    IP 地址            丢包率    平均延迟    下载速度      地区码    地区
------------------------------------------------------------------------------
1       104.19.55.123      0%        50 ms       82.63 Mbps   HKG      香港
```

### CSV 文件

```csv
IP,丢包率,平均延迟(ms),下载速度(Mbps),地区码,地区
104.19.55.123,0.00%,50,82.63,HKG,香港
```

## 地区码说明

- **默认模式（ICMP/TCPing）**：地区码在**下载测速**时从 HTTP 响应头解析
- **HTTPing 模式**：地区码在延迟测速时即可获取，支持 `-cfcolo` 过滤
- 支持 CDN：Cloudflare、AWS CloudFront、Fastly、CDN77、Bunny、Gcore

## 常见问题

**Q: 本地没有 ip.txt 怎么办？**  
A: 程序会自动从 [CloudflareIP-Sync](https://github.com/codingriver/CloudflareIP-Sync) 经 jsDelivr CDN 下载，并保存到当前目录。

**Q: 如何只测延迟不测下载？**  
A: 使用 `-dd` 参数。

**Q: TCPing 和 ICMP 有什么区别？**  
A: ICMP 需要系统允许 Ping；TCPing 走 TCP 443 端口，无需特殊权限，推荐在受限环境使用。

**Q: 测速 URL 用 HTTP 还是 HTTPS？**  
A: 根据 URL 协议自动选择端口（HTTP=80，HTTPS=443），需用 `-tp` 与 URL 一致。

## 参考

- [Cloudflare IP 段](https://www.cloudflare.com/ips-v4)

---

## 开源协议

本项目采用 [MIT License](LICENSE) 开源协议。您可自由使用、修改、分发本软件，但需保留版权声明。

## 版权声明

Copyright (c) 2024-2025

基于 [XIU2/CloudflareSpeedTest](https://github.com/XIU2/CloudflareSpeedTest) 的 C# 实现。原项目采用 GPL-3.0 协议。

## 免责声明

- 本工具仅供学习、研究与合法网络优化用途，请遵守当地法律法规及 Cloudflare 服务条款。
- 作者不对使用本工具造成的任何直接或间接损失负责。
- 测速结果受网络环境、运营商策略等因素影响，仅供参考。
