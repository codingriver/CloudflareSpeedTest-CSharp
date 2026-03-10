# CloudflareST CLI 使用说明

## 概述

CLI 通过 `ConfigMapper.FromArgs(args)` 将命令行参数映射为 `TestConfig`，然后调用 `CoreService.RunTestAsync`。

## 运行方式

```powershell
# 使用默认参数
dotnet run --project CloudflareST.Cli/CloudflareST.Cli.csproj

# 指定参数
dotnet run --project CloudflareST.Cli/CloudflareST.Cli.csproj -- -n 8 -t 2 -tcping -url "https://speed.cloudflare.com/__down?bytes=52428800" -o result.csv

# 使用发布的可执行文件
.\publish\win-x64\cfst.exe -n 200 -tcping -o result.csv
./publish/linux-x64/cfst -tcping -silent
```

## 参数映射表

| 参数 | 默认值 | TestConfig 属性 | 说明 |
|---|---|---|---|
| `-n <数字>` | 4 | `Concurrency` | 延迟测速并发数 |
| `-t <数字>` | 4 | `RunsPerIp` | 单 IP 测速次数 |
| `-tcping` | false | `UseTcping` | 使用 TCPing（TCP 443） |
| `-httping` | false | `UseHttping` | 使用 HTTPing |
| `-ipv6` | false | `UseIpv6` | 启用 IPv6 |
| `-f <路径>` | ip.txt | `IpSourceFiles` | IPv4 段文件 |
| `-f6 <路径>` | ipv6.txt | `IpSourceFiles` + `UseIpv6` | IPv6 段文件 |
| `-ipn <数字>` | 0 | `IpLimit` | IP 加载数量上限（0=不限） |
| `-url <URL>` | Cloudflare 官方 | `Url` | 测速下载地址 |
| `-tp <端口>` | 443 | `Tp` | 测速端口 |
| `-o <路径>` | result.csv | `OutputFile` | 输出 CSV 文件 |
| `-p <数字>` | 10 | `OutputLimit` | 最终输出 IP 数量 |
| `-silent`/`-q` | false | `Silent` | 静默模式：仅输出 IP |
| `-debug` | false | `Debug` | 调试输出 |
| `-interval <分钟>` | 0 | `IntervalMinutes` | 间隔调度 |
| `-at "hh:mm,..."` | — | `AtTimes` | 每日定点 |
| `-cron "表达式"` | — | `Cron` | Cron 调度 |
| `-tz <时区>` | local | `Timezone` | 时区 |
| `-hosts <表达式>` | — | `HostsExpr` | Hosts 更新 |
| `-hosts-dry-run` | false | `HostsDryRun` | 仅预览，不写入 |

## 打包与发布

```powershell
# Windows — 全平台 self-contained（默认）
.\build.ps1

# 仅 Windows x64
.\build.ps1 win-x64

# Framework-dependent（体积小，需本机安装 .NET 8）
.\build.ps1 -fd

# 构建 Unity DLL 并复制到 Assets/Plugins/CloudflareST
.\build.ps1 -unity

# 清理
.\build.ps1 -clean
```

```bash
# Linux/macOS
./build.sh
./build.sh linux-x64
./build.sh -fd
./build.sh -clean
```

输出在 `publish/` 目录下，文件命名规则：
- `cfst-win-x64.exe`
- `cfst-linux-x64`
- `cfst-osx-x64`
- `cfst-osx-arm64`
- `cfst-win-x64-fd.exe`（fd 版本）

## 未识别参数行为

未识别的参数会被收集到 `TestConfig.UnknownFlags`，并在 stderr 输出警告：

```
Warning: Unknown CLI flags: -unknown-flag
```

这保证了向前兼容性：新版 Core 添加参数后旧版 CLI 不会崩溃。
