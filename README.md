# Cloudflare Speed Test - C# 版支持Hosts和定时任务

从 Cloudflare CDN 的众多 IP 中，帮你测出**延迟低、速度快**的节点，方便自建优选或配置代理。基于 [XIU2/CloudflareSpeedTest](https://github.com/XIU2/CloudflareSpeedTest) 的 C# 实现。

## 快速开始

### Windows

在 [Release 页面](https://github.com/codingriver/CloudflareSpeedTest-CSharp/releases) 下载 `cfst-win-x64.exe`，双击或命令行运行：

```powershell
.\cfst-win-x64.exe
```

**测速方式**：默认用 ICMP Ping。如果测不出结果或结果异常，可能是网络禁用了 ICMP，可改用 `-tcping` 或 `-httping`。

```powershell
# 以下任选其一
.\cfst-win-x64.exe                                    # ICMP Ping（默认）
.\cfst-win-x64.exe -tcping                             # TCPing：走 TCP 443，无需 ICMP 权限
.\cfst-win-x64.exe -httping                            # HTTPing（使用默认 URL）
.\cfst-win-x64.exe -httping -url "https://speed.cloudflare.com/__down?bytes=52428800"  # HTTPing（Cloudflare 官方测速）
```

### Linux

在 [Release 页面](https://github.com/codingriver/CloudflareSpeedTest-CSharp/releases) 下载 `cfst-linux-x64` 后执行：

```bash
chmod +x cfst-linux-x64
./cfst-linux-x64
```

**测速方式**：默认 ICMP Ping。测不出结果时改用 `-tcping` 或 `-httping`。

```bash
./cfst-linux-x64                                      # ICMP Ping（默认）
./cfst-linux-x64 -tcping                               # TCPing：走 TCP 443，无需 ICMP 权限
./cfst-linux-x64 -httping                              # HTTPing（使用默认 URL）
./cfst-linux-x64 -httping -url "https://speed.cloudflare.com/__down?bytes=52428800"  # HTTPing（Cloudflare 官方测速）
```

### macOS

在 [Release 页面](https://github.com/codingriver/CloudflareSpeedTest-CSharp/releases) 下载：Intel 用 `cfst-osx-x64`，Apple Silicon 用 `cfst-osx-arm64`。

```bash
chmod +x cfst-osx-x64    # 或 cfst-osx-arm64
./cfst-osx-x64           # 或 ./cfst-osx-arm64
```

**测速方式**：默认 ICMP Ping。测不出结果时改用 `-tcping` 或 `-httping`。

```bash
# Intel
./cfst-osx-x64                                        # ICMP Ping（默认）
./cfst-osx-x64 -tcping                                 # TCPing：走 TCP 443，无需 ICMP 权限
./cfst-osx-x64 -httping                                # HTTPing（使用默认 URL）
./cfst-osx-x64 -httping -url "https://speed.cloudflare.com/__down?bytes=52428800"  # HTTPing（Cloudflare 官方测速）

# Apple Silicon
./cfst-osx-arm64                                       # ICMP Ping（默认）
./cfst-osx-arm64 -tcping                                # TCPing
./cfst-osx-arm64 -httping                               # HTTPing（使用默认 URL）
./cfst-osx-arm64 -httping -url "https://speed.cloudflare.com/__down?bytes=52428800"  # HTTPing（Cloudflare 官方测速）
```

首次运行会自动下载 Cloudflare IP 列表到 `ip.txt`，无需手动准备。

## 常用示例

### 只测延迟

```bash
./cfst -dd
./cfst -tcping -dd
./cfst -httping -dd
```

### 换测速地址

```bash
# 用 HTTP 测速（端口 80）
./cfst -url "http://speedtest.303066.xyz/__down?bytes=104857600" -tp 80
./cfst -url "http://speed.cloudflare.com/__down?bytes=52428800"

# 用 Cloudflare 官方测速（推荐）
./cfst -url "https://speed.cloudflare.com/__down?bytes=52428800"
```

### 限制测速 IP 数量

```bash
./cfst -ipn 2000
./cfst -ipn 500 -dd
```

### 指定 IP 段

```bash
./cfst -ip "173.245.48.0/20,104.16.0.0/13"
./cfst -f my-ip.txt
```

### 按地区筛选（仅 HTTPing）

```bash
./cfst -httping -cfcolo "HKG,NRT,LAX"
```

### 调高并发

```bash
./cfst -allip
./cfst -n 500 -dn 20 -t 8
```

### 静默模式（只输出 IP）

```bash
./cfst -silent
./cfst -q
```

适合脚本、管道等自动化场景。

### 定时调度

```bash
# 每 6 小时执行一次
./cfst -interval 360 -silent -dd

# 每天 6:00、12:00、18:00 执行
./cfst -at "6:00,12:00,18:00" -silent

# Cron 表达式：每 6 小时整点
./cfst -cron "0 */6 * * *" -silent -dd

# Cron：每周一 0:00
./cfst -cron "0 0 * * 1" -silent
```

### Hosts 自动更新

```bash
# 测速后更新 hosts，有则更新、无则添加
./cfst -hosts "cdn.example.com,*.example.com" -silent

# 使用第 2 名 IP，仅预览不写入
./cfst -hosts "a.com" -hosts-ip 2 -hosts-dry-run
```

### 定时调度 + Hosts 综合示例

```bash
# TCPing 测速，每 12 小时执行，更新 hosts
./cfst -tcping -interval 720 -hosts "a.b.com" -silent 

# HTTPing 测速，每天早上 6 点执行，更新 hosts
./cfst -httping -at "6:00" -hosts "a.b.com,c.b.com" -silent

# TCPing 测速，每周一早上 5:30 执行，更新 hosts（Cron 格式：分 时 日 月 周）
# *.d.com代表是更新hosts中匹配到d.com的域名，如果没有则不更新也不写入新的，可以手动先加好在用这个
./cfst -tcping -cron "30 5 * * 1" -hosts "*.d.com" -silent 

# HTTPing 测速，每天 6:00 和 18:00，多域名 hosts
./cfst -httping -at "6:00,18:00" -hosts "cdn.example.com,api.example.com,*.example.com"
```

**注意**：  
- 在 **Windows** 上，更新 hosts 会写入 `C:\Windows\System32\drivers\etc\hosts`，需要**以管理员身份运行**终端或双击 exe；  
- 在 **Linux** 上，更新 hosts 会写入 `/etc/hosts`，需要使用 **root 用户或 `sudo`** 运行；  
- 在 **macOS** 上，同样写入 `/etc/hosts`，需要使用 **sudo/root** 运行。  
如果权限不足，程序不会直接修改系统 hosts，而是把待写入内容输出到当前目录的 `hosts-pending.txt`，可手动合并。

## 功能

- **三种测速方式**：ICMP Ping（默认）、TCPing（无需 ICMP 权限）、HTTPing（可测地区码）
- **下载测速**：测延迟后还会测下载速度，结果更直观
- **地区码**：支持 Cloudflare、AWS、Fastly、CDN77、Bunny、Gcore
- **IP 列表**：缺 `ip.txt` 时自动从 CloudflareIP-Sync 下载
- **CSV 导出**：含地区码、地区中文名
- **定时调度**：支持 `-interval` 间隔、`-at` 每日定点、`-cron` Cron 表达式
- **Hosts 更新**：`-hosts "a.com,*.b.com"` 自动更新/添加 hosts 条目

## 环境

- Windows / Linux / macOS
- 无需安装 .NET：直接下载 Release 的可执行文件即可

## 编译

```bash
dotnet build
```

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
| `-url` | Cloudflare 官方 | 测速下载地址 |
| `-tp` | 443 | 测速端口（HTTP 用 80） |
| `-dn` | 10 | 参与下载测速的 IP 数（仅决定实际测速数量，不再影响最终可用 IP 数量） |
| `-dt` | 10 | 下载测速超时(秒) |
| `-sl` | 0 | 速度下限(MB/s)，低于则过滤 |
| `-dd` | false | 禁用下载测速 |
| **输出** | | |
| `-o` | result.csv | 输出 CSV 文件 |
| `-p` | 10 | 最终输出的 IP 数量上限（控制台、CSV、静默模式 onlyip.txt 均受此限制；传 0 或负数时按 10 处理） |
| **其他** | | |
| `-allip` | false | 全量 IP（默认每/24 随机一个） |
| `-debug` | false | 调试输出 |
| `-silent` / `-q` | false | 静默模式：只输出 IP，每行一个，适合脚本调用 |
| `-onlyip` | onlyip.txt | 静默模式下的 IP 输出文件 |
| **定时调度** | | |
| `-interval` | 0 | 间隔分钟数，>0 时每 N 分钟执行一次 |
| `-at` | - | 每日定点，如 "6:00,18:00" |
| `-cron` | - | Cron 表达式 |
| `-tz` | 本地 | 时区（仅 -at/-cron 适用） |
| **Hosts** | | |
| `-hosts` | - | 要更新/添加的域名，如 "a.com,*.b.com" |
| `-hosts-ip` | 1 | 使用测速结果第 N 名 IP |
| `-hosts-file` | 系统默认 | 自定义 hosts 路径 |
| `-hosts-dry-run` | false | 仅输出待写入内容，不实际写入 |

## 自行打包

需要从源码构建时，可用脚本一键打包：

```powershell
# Windows
.\build\build.ps1
.\build\build.ps1 win-x64
.\build\build.ps1 -fd           # 依赖框架版，体积小，需本机安装 .NET 8
```

```bash
# Linux / macOS
./build/build.sh
./build/build.sh win-x64
./build/build.sh -fd
```

输出在 `publish/` 目录下。

## 项目结构

```
CloudflareSeedTest-CSharp/
├── CloudflareST.csproj
├── Program.cs        # 主程序入口
├── Config.cs         # 配置
├── IPInfo.cs         # 数据结构
├── IpProvider.cs     # IP 列表加载
├── IcmpPinger.cs    # ICMP Ping 延迟测试（默认）
├── PingTester.cs    # TCPing 延迟测试
├── HttpingTester.cs # HTTPing 延迟测试
├── ColoProvider.cs  # CDN 地区码解析
├── SpeedTester.cs   # HTTP 下载测速
├── OutputWriter.cs  # 结果输出
├── Scheduler.cs     # 定时调度（interval/at/cron）
├── HostsUpdater.cs # Hosts 更新
├── build/          # 打包脚本
└── docs/           # 使用文档
```

## 输出说明

### 控制台

运行后会显示表格，包含 IP、丢包率、延迟、下载速度、地区码等：

```
序号    IP 地址            丢包率    平均延迟    下载速度      地区码    地区
------------------------------------------------------------------------------
1       104.19.55.123      0%        50 ms       82.63 Mbps   HKG      香港
```

### CSV 文件

结果会保存到 `result.csv`（可用 `-o` 指定），方便导入 Excel 或脚本：

```csv
IP,丢包率,平均延迟(ms),下载速度(Mbps),地区码,地区
104.19.55.123,0.00%,50,82.63,HKG,香港
```

## 地区码说明

- **ICMP/TCPing**：地区码在下载测速时从 HTTP 响应头解析
- **HTTPing**：测延迟时就能拿到地区码，可用 `-cfcolo` 筛选（如 HKG、NRT、LAX）
- 支持 Cloudflare、AWS、Fastly、CDN77、Bunny、Gcore

## 常见问题

**Q: 没有 ip.txt 怎么办？**  
A: 首次运行会自动下载，无需手动准备。

**Q: 只想测延迟，不测下载？**  
A: 加 `-dd` 参数。

**Q: TCPing 和 ICMP 有啥区别？**  
A: ICMP 需要系统允许 Ping；TCPing 走 TCP 443，不依赖 ICMP，网络受限时更稳。

**Q: 测速 URL 用 HTTP 还是 HTTPS？**  
A: 都行，端口要对应：HTTP 用 80，HTTPS 用 443，用 `-tp` 指定。

**Q: 修改 hosts 提示无权限？**  
A: Windows 需以管理员运行；Linux/macOS 需 root 或 sudo。无权限时内容会输出到 `hosts-pending.txt`。

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
