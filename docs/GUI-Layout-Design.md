# Cloudflare Speed Test — GUI 布局设计文档

> **文档说明**
> 本文档基于 `CloudflareSeedTest-CSharp` 项目的参数体系（`CfstOptions.cs`）和功能说明，
> 设计桌面 GUI 布局方案，供 WinForms / WPF / MAUI / Avalonia 等框架实现参考。
> 采用**左侧竖向导航 + 主内容区**布局，共 8 个导航页。

---

## 整体窗口结构

```
+-------------------------------------------------------------------------------+
|  [*] Cloudflare Speed Test                              [-] [O] [X]           |
+--------------------+----------------------------------------------------------+
| [>] IP 来源        | （主内容区，随左侧导航切换）                              |
| [ ] 延迟测速       |                                                          |
| [ ] 下载测速       |                                                          |
| [ ] 定时调度       |                                                          |
| [ ] Hosts 更新     |                                                          |
| [ ] 输出设置       |                                                          |
| [ ] 其他设置       |                                                          |
| ----------------   |                                                          |
| [ ] 测速结果 [10]  |  <- 角标显示结果 IP 数量                                |
| ----------------   |                                                          |
| [▶ 开始测速     ]  |                                                          |
| [■  停  止      ]  |                                                          |
| [=========  ] 78%  |  <- 全局进度条，任意页可见                               |
| 下载测速中...      |                                                          |
|                    +----------------------------------------------------------+
|                    | @ 就绪 | 已测:-- | 耗时:-- | 当前最快:--               |
+--------------------+----------------------------------------------------------+
```

### 布局分区说明

| 区域       | 宽度占比 | 说明                                     |
|------------|---------|------------------------------------------|
| 左侧导航栏 | ~20%    | 固定宽度；竖向菜单 + 全局操作按钮 + 进度条  |
| 主内容区   | ~80%    | 随导航切换，显示对应配置页或结果页          |
| 底部状态栏 | 100%    | 固定在主内容区底部，一行显示实时状态摘要    |

---

## 全局数据结构绑定概述

所有页面的控件最终都读写同一个 `CfstOptions` 实例（简称 `opts`）。  
GUI 层在点击「开始测速」时将 `opts` 通过 `CfstOptionsExtensions.ToArguments()` 序列化为命令行参数，传给 `CfstProcessManager.StartAsync()` 启动外部 cfst.exe 进程。

**数据流图：**

```
GUI 控件  <--双向绑定-->  CfstOptions (opts)  --ToArguments()-->  cfst.exe 参数
                                                                        |
                                                              CfstProcessManager
                                                           (启动/监听/停止子进程)
                                                                        |
                                                         stdout 按行 OnOutput 回调
                                                    ├── PROGRESS:{json} -> 进度条/状态栏
                                                    └── 其余行 -> 运行日志文本框
```

**数据流向步骤：**
1. 用户修改控件 → 立即写入 `opts` 对应字段
2. 点击「开始测速」→ 验证必填字段 → `CfstOptionsExtensions.ToArguments(opts)` 生成参数串
3. `CfstProcessManager.StartAsync(opts)` 启动 cfst.exe 子进程，监听 stdout/stderr
4. 进程 stdout 每行通过 `OnOutput` 回调分流：`PROGRESS:` 前缀行解析 JSON 驱动进度，其余行追加到日志
5. `OnExited` 触发后 → 侧栏角标、状态栏、结果页统一更新

---

## 左侧导航栏

```
+--------------------+
| [*] CFST           |  <- 应用标题 / Logo
+--------------------+
| [>] IP 来源        |  <- 当前激活页（高亮背景）
|  .  延迟测速       |
|  .  下载测速       |
|  .  定时调度       |
|  .  Hosts 更新     |
|  .  输出设置       |
|  .  其他设置       |
|  ----------------  |
|  .  测速结果 [10]  |  <- 角标：测速完成后显示结果 IP 数
+--------------------+
| [▶  开始测速    ]  |  <- 主操作按钮（绿色）
| [■    停  止    ]  |  <- 停止按钮（红色，运行中才激活）
| [=========  ] 78%  |  <- 全局进度条（绑定 AppState.Progress 0-100）
| 下载测速中...      |  <- 全局状态文字（绑定 AppState.StatusText）
+--------------------+
```

### 导航栏交互逻辑

| 元素 | 交互行为 | 数据/状态来源 |
|------|---------|---------------|
| 导航菜单项 | 单击切换主内容区，激活项高亮；切换不影响 `opts` | `AppState.CurrentPage` |
| 测速结果 `[N]` 角标 | 测速完成后出现，数字 = `TestResult.IpList.Count`；点击跳转结果页 | `TestResult.IpList.Count` |
| **▶ 开始测速** | 验证字段 → `CfstProcessManager.StartAsync(opts)` → 按钮禁用；进度条/状态开始刷新 | 触发 `CfstProcessManager` |
| **■ 停止** | 仅运行中可点；`CfstProcessManager.Stop()` → 等待退出 → 恢复开始按钮 | `AppState.IsRunning` |
| 全局进度条 | 绑定 `AppState.Progress`（0~100），由 `PROGRESS:{json}` 解析驱动 | `AppState.Progress` |
| 全局状态文字 | 绑定 `AppState.StatusText`：「延迟测速中...」「下载测速中...」「就绪」等 | `AppState.StatusText` |

**开始/停止按钮互斥逻辑：**
```
运行中：  开始按钮 Enabled=false，停止按钮 Enabled=true
空闲中：  开始按钮 Enabled=true，  停止按钮 Enabled=false
```

---

## 底部状态栏

```
@ 就绪 | 已测: 486/2000 | 耗时: 01:31 | 当前最快: 50ms / 82.63 Mbps
```

| 字段 | 数据绑定 | 更新时机 |
|------|---------|----------|
| 运行状态 | `AppState.StatusText` | 阶段切换时（就绪/延迟测速中/下载测速中/已完成/已停止）|
| 已测 N/Total | `AppState.TestedCount` / `AppState.TotalCount` | `ping` 进度消息每次更新 |
| 耗时 | `AppState.Elapsed`（UI 定时器每秒刷新）| 开始时启动，结束/停止时冻结，格式 `mm:ss` |
| 当前最快 | `AppState.BestLatency` / `AppState.BestSpeed` | 从 `done` 消息的 `results[]` 实时取最值 |

---

## 页面 1 — IP 来源

对应模块：`IpProvider.cs` / `Config.cs`  
对应 `CfstOptions` 字段：`IpFiles` / `IpRanges` / `IpLoadLimit` / `AllIp`

```
+-----------------------------------------------------------------------------+
|  IP 来源                                                                    |
| ---------------------------------------------------------------------------  |
|  IPv4 文件 (-f) :  [ip.txt                              ] [浏览...]        |
|  IPv6 文件 (-f6):  [ipv6.txt                            ] [浏览...]        |
|                                                                             |
|  直接指定 IP 段 (-ip)  <- 优先级高于文件，留空则读取文件                   |
|  +-------------------------------------------------------------------------+|
|  | 173.245.48.0/20,104.16.0.0/13                                           ||
|  +-------------------------------------------------------------------------+|
|  逗号分隔多个 CIDR 段，支持 IPv4/IPv6 混合；留空则读取上方 IP 文件         |
|                                                                             |
|  IP 数量上限 (-ipn):  [_______0________]   0 = 不限制                     |
|  [_] 全量扫描 (-allip)  扫描每个/24段全部IP（默认每段随机取1个）           |
+-----------------------------------------------------------------------------+
```

### 控件交互逻辑

**IPv4 / IPv6 文件路径（TextBox）**
- 实时写入 `opts.IpFiles[0]` / `opts.IpFiles[1]`；「浏览...」打开 `OpenFileDialog`（过滤 `*.txt`）
- 文件不存在时显示红色边框 + tooltip「文件不存在，将在运行时报错」
- `opts.IpRanges` 非空时右侧显示浅色提示「已忽略（直接 IP 段优先）」，控件不禁用

**直接指定 IP 段（TextBox 多行）**
- 实时写入 `opts.IpRanges`；清空时写入 `null`
- 非空 → 文件行加「已忽略」提示
- 格式校验：逗号分割后验证每段是否为合法 CIDR；无效段红色下划线标出

**IP 数量上限（NumericUpDown）**
- 绑定 `opts.IpLoadLimit`，最小值 0，步进 100；值为 0 时右侧显示「0 = 不限制」

**全量扫描（CheckBox）**
- 绑定 `opts.AllIp`；勾选时显示橙色警告「扫描量大幅增加，耗时可能超过10分钟」
- 建议配合 IP 数量上限控制总量

---

## 页面 2 — 延迟测速

对应模块：`IcmpPinger.cs` / `PingTester.cs` / `HttpingTester.cs`  
对应 `CfstOptions` 字段：`PingMode` / `ForceIcmp` / `PingConcurrency` / `PingCount` / `LatencyMax` / `LatencyMin` / `PacketLossMax` / `HttpingCode` / `CfColo`

```
+-----------------------------------------------------------------------------+
|  延迟测速                                                                   |
| ---------------------------------------------------------------------------  |
|  测速方式：                                                                 |
|  (*) ICMP Ping（默认，不可用时自动切换 TCPing）                            |
|      [_] 强制 ICMP (-icmp)  即使检测到无权限也不切换 TCPing               |
|  ( ) TCPing (-tcping)  TCP 443 端口，不依赖 ICMP 权限                     |
|  ( ) HTTPing (-httping)  HTTP HEAD 请求，可同时解析 CDN 地区码            |
|      有效状态码 (-httping-code): [__0__]  0=接受200/301/302              |
|      地区码过滤 (-cfcolo):       [HKG,NRT,LAX          ]                  |
| ---------------------------------------------------------------------------  |
|  并发数 (-n):      [___200___]   单IP测速次数 (-t):  [__4__]              |
|  延迟上限 (-tl):   [__9999___] ms   延迟下限 (-tll): [__0__] ms           |
|  丢包率上限 (-tlr):[__100____] %   (1.0 = 不过滤任何IP)                  |
+-----------------------------------------------------------------------------+
```

### 控件交互逻辑

**测速方式（RadioButton 三选一）**
- 绑定 `opts.PingMode`（IcmpAuto / TcPing / Httping）
- 选中 IcmpAuto → 强制ICMP复选框 Enabled=true；其余模式时置灰并同步 `opts.ForceIcmp=false`
- 选中 Httping → HTTPing 专属区域（状态码、地区码）Enabled=true；其他模式时置灰
- 选中 TcPing → 强制ICMP和HTTPing专属区域均置灰

**强制 ICMP（CheckBox）**：绑定 `opts.ForceIcmp`；仅 IcmpAuto 模式可编辑

**并发数（NumericUpDown）**：绑定 `opts.PingConcurrency`，范围 1~1000，步进 50

**单IP测速次数（NumericUpDown）**：绑定 `opts.PingCount`，范围 1~20，步进 1

**延迟上限/下限（NumericUpDown）**
- 绑定 `opts.LatencyMax` / `opts.LatencyMin`
- 实时校验：`LatencyMax` 必须 > `LatencyMin`，否则红框提示

**丢包率上限（NumericUpDown）**
- 绑定 `opts.PacketLossMax`；界面以百分比显示（0.1 显示为 10%），存储为小数
- 范围 0~100%，步进 10%

**HTTPing 状态码（NumericUpDown）**：绑定 `opts.HttpingCode`；仅 Httping 模式可编辑；0 时右侧显示「接受 200/301/302」

**地区码过滤（TextBox）**：绑定 `opts.CfColo`；仅 Httping 模式可编辑；输入时以逗号分割大写校验；无效地区码红色下划线

---

## 页面 3 — 下载测速

对应模块：`SpeedTester.cs`  
对应 `CfstOptions` 字段：`DisableDownload` / `DownloadUrl` / `DownloadPort` / `DownloadCount` / `DownloadTimeout` / `SpeedMin`

```
+-----------------------------------------------------------------------------+
|  下载测速                                                                   |
| ---------------------------------------------------------------------------  |
|  [_] 禁用下载测速 (-dd)  只测延迟，跳过下载，大幅缩短耗时                 |
| ---------------------------------------------------------------------------  |
|  测速 URL (-url):                                                           |
|  [https://speed.cloudflare.com/__down?bytes=52428800               ]       |
|  测速端口 (-tp):  [__443__]   (HTTP 用 80，HTTPS 用 443)                  |
|  参与测速 IP 数 (-dn):  [__10__]   (从延迟通过的 IP 中取前 N 个)          |
|  下载超时 (-dt):  [__10__] 秒                                              |
|  速度下限 (-sl):  [___0___] MB/s   0 = 不过滤                             |
+-----------------------------------------------------------------------------+
```

### 控件交互逻辑

**禁用下载测速（CheckBox）**
- 绑定 `opts.DisableDownload`
- 勾选 → URL/端口/IP数/超时/速度下限全部 `Enabled=false`
- 取消勾选 → 全部恢复 `Enabled=true`，各字段值保持不变

**测速 URL（TextBox）**
- 绑定 `opts.DownloadUrl`；失去焦点时做 `Uri.TryCreate` 合法性校验，非法则红框
- URL 协议联动端口提示：`http://` 开头时旁边显示「建议将端口改为 80」；`https://` 时显示「建议端口 443」

**测速端口（NumericUpDown）**：绑定 `opts.DownloadPort`，范围 1~65535，步进 1；右键菜单提供「设为 80」「设为 443」快捷项

**参与测速 IP 数（NumericUpDown）**：绑定 `opts.DownloadCount`，范围 1~100，步进 1；右侧提示「从延迟测速通过的 IP 中取前 N 个参与下载测速」

**下载超时（NumericUpDown）**：绑定 `opts.DownloadTimeout`，范围 1~120，步进 1，单位秒

**速度下限（NumericUpDown）**：绑定 `opts.SpeedMin`，单位 MB/s，最小 0，步进 1；值为 0 时右侧显示「0 = 不过滤」

---

## 页面 4 — 定时调度

对应模块：`Scheduler.cs`  
对应 `CfstOptions` 字段：`ScheduleMode` / `IntervalMinutes` / `DailyAt` / `CronExpression` / `TimeZone`

```
+-----------------------------------------------------------------------------+
|  定时调度                                                                   |
| ---------------------------------------------------------------------------  |
|  调度模式（四选一）                                                         |
|  (*) 不启用（执行一次即退出）                                               |
|  ( ) 间隔执行 (-interval)  每隔 [___60___] 分钟执行一次                   |
|  ( ) 每日定点 (-at)                                                         |
|      时间点: [6:00,12:00,18:00                ]  逗号分隔多个时间点   |
|  ( ) Cron 表达式 (-cron)                                                    |
|      [0 */6 * * *                             ]  格式：分 时 日 月 周       |
| ---------------------------------------------------------------------------  |
|  时区 (-tz):  [Local -- 系统默认                    v]  仅 -at/-cron 适用  |
| ---------------------------------------------------------------------------  |
|  -- 调度预览（只读）-------------------------------------------------------- |
|  下次执行:  2026-03-16 06:00:00                                             |
|  再下次  :  2026-03-16 12:00:00                                             |
+-----------------------------------------------------------------------------+
```

### 控件交互逻辑

**调度模式（RadioButton 四选一）**
- 绑定 `opts.ScheduleMode`（None / Interval / Daily / Cron）
- None → 所有参数控件及时区下拉均 `Enabled=false`；预览区显示「--」
- Interval → 间隔分钟数 `Enabled=true`；定点/Cron/时区 `Enabled=false`
- Daily → 定点时间 `Enabled=true`；间隔/Cron `Enabled=false`；时区 `Enabled=true`
- Cron → Cron 输入框 `Enabled=true`；间隔/定点 `Enabled=false`；时区 `Enabled=true`
- 切换模式时不清空未激活控件的值，便于切回时恢复

**间隔分钟数（NumericUpDown）**：绑定 `opts.IntervalMinutes`，范围 1~10080（7天），步进 1；仅 Interval 模式 Enabled

**每日定点时间（TextBox）**
- 绑定 `opts.DailyAt`；仅 Daily 模式 Enabled
- 失去焦点时校验每个时间点格式（`HH:mm` 或 `H:mm`）；无效项红色下划线；合法时刷新预览

**Cron 表达式（TextBox）**
- 绑定 `opts.CronExpression`；仅 Cron 模式 Enabled
- 失去焦点时用 Cronos 库校验；无效则红框 + tooltip 显示具体错误；合法时刷新预览

**时区（ComboBox）**
- 绑定 `opts.TimeZone`；仅 Daily / Cron 模式 Enabled
- 列表来源：`TimeZoneInfo.GetSystemTimeZones()`；首项「Local -- 系统默认」值为 null

**调度预览（只读 TextBlock）**
- 参数合法时本地计算显示「下次执行」「再下次」时间；非法或 None 时显示「--」
- Interval：下次 = 当前时间 + N 分钟
- Daily / Cron：使用 Cronos 库计算下两次触发时间

---

## 页面 5 — Hosts 更新

对应模块：`HostsUpdater.cs`  
对应 `CfstOptions` 字段：`HostsDomains` / `HostsIpRank` / `HostsFile` / `HostsDryRun`

```
+-----------------------------------------------------------------------------+
|  Hosts 更新                                                                 |
| ---------------------------------------------------------------------------  |
|  [v] 启用 Hosts 更新                                                        |
|  目标域名 (-hosts):                                                         |
|  [cdn.example.com,*.example.com                                     ]      |
|  逗号分隔；* 通配符只更新已有条目，无匹配则不新增                          |
|                                                                             |
|  使用第 (-hosts-ip):  [__1__]  名 IP  (1 = 最快 IP)                       |
|  Hosts 文件路径 (-hosts-file):  [                    ] [浏览...]           |
|  留空则使用系统默认路径                                                     |
|  [_] 仅预览不写入 (-hosts-dry-run)                                         |
| ---------------------------------------------------------------------------  |
|  [!] Windows 需以管理员身份运行；Linux/macOS 需 root 或 sudo。             |
|      权限不足时内容输出到 hosts-pending.txt，可手动合并。                  |
+-----------------------------------------------------------------------------+
```

### 控件交互逻辑

**启用 Hosts 更新（CheckBox）**
- 未勾选时 `opts.HostsDomains = null`（序列化时不生成 -hosts 参数）
- 勾选时目标域名/IP排名/文件路径/仅预览 均 `Enabled=true`
- 取消勾选时以上控件 `Enabled=false`，但不清空值

**目标域名（TextBox 多行）**
- 绑定 `opts.HostsDomains`；逗号分隔，支持 `*` 通配符
- 清空时同步取消「启用 Hosts 更新」勾选

**使用第 N 名 IP（NumericUpDown）**：绑定 `opts.HostsIpRank`，范围 1~100，步进 1；右侧说明「将测速排名第 N 的 IP 写入 hosts」

**Hosts 文件路径（TextBox）**
- 绑定 `opts.HostsFile`；留空时写入 null，运行时使用系统默认路径
- 「浏览...」按钮打开文件选择对话框；路径不存在时显示黄色警告

**仅预览不写入（CheckBox）**：绑定 `opts.HostsDryRun`；勾选时结果页 Hosts 区域标题显示「(仅预览，未实际写入)」

**权限提示区（InfoBar，始终显示）**
- 根据 OS 动态切换文字：Windows 显示管理员提示；Linux/macOS 显示 sudo 提示
- 启动时检测当前进程是否有管理员/root 权限；无权限时显示橙色警告图标

---

## 页面 6 — 输出设置

对应模块：`OutputWriter.cs` / `ConsoleHelper.cs`  
对应 `CfstOptions` 字段：`OutputFile` / `OutputDir` / `OutputCount` / `Silent` / `OnlyIpFile`

```
+-----------------------------------------------------------------------------+
|  输出设置                                                                   |
| ---------------------------------------------------------------------------  |
|  输出 CSV 文件 (-o):  [result.csv                  ] [浏览...]             |
|  统一输出目录 (-outputdir):  [                     ] [浏览...]             |
|  留空则输出到工作目录；指定后 CSV 和 onlyip.txt 均输出到该目录            |
|                                                                             |
|  最终输出 IP 数 (-p):  [__10__]                                            |
|  控制台表格、CSV、onlyip.txt 均受此限制；传 0 或负数按 10 处理            |
|                                                                             |
|  [_] 静默模式 (-silent/-q)  只输出 IP，不显示进度表格，适合脚本/管道      |
|      onlyip 文件 (-onlyip):  [onlyip.txt           ] [浏览...]             |
+-----------------------------------------------------------------------------+
```

### 控件交互逻辑

**输出 CSV 文件（TextBox）**：绑定 `opts.OutputFile`；「浏览...」打开 `SaveFileDialog`（*.csv）；目录不存在时黄色警告「将在运行时自动创建」

**统一输出目录（TextBox）**：绑定 `opts.OutputDir`；留空=null；「浏览...」打开 `FolderBrowserDialog`

**最终输出 IP 数（NumericUpDown）**：绑定 `opts.OutputCount`，范围 1~1000，步进 1

**静默模式（CheckBox）**
- 绑定 `opts.Silent`
- 勾选 → onlyip 文件行 `Enabled=true`；显示提示「只向 stdout 逐行输出 IP，不显示表格，适合脚本/管道调用」
- 取消勾选 → onlyip 文件行 `Enabled=false`

**onlyip 文件路径（TextBox）**：绑定 `opts.OnlyIpFile`；仅 `Silent=true` 时 Enabled；「浏览...」打开 `SaveFileDialog`（*.txt）

---

## 页面 7 — 其他设置

对应模块：`ConsoleHelper.cs` / `ProgressReporter.cs`  
对应 `CfstOptions` 字段：`Debug` / `ShowProgress`

```
+-----------------------------------------------------------------------------+
|  其他设置                                                                   |
| ---------------------------------------------------------------------------  |
|  [_] 调试输出 (-debug)                                                      |
|      启用后打印详细内部状态，HTTPing 模式额外输出每个 IP 的状态码和异常    |
|                                                                             |
|  [v] 结构化进度输出 (-progress)                                             |
|      启用后 cfst 在 stdout 输出 PROGRESS:{json} 行，驱动 GUI 进度条        |
|      与静默模式正交：-silent -progress 时只输出 PROGRESS 行和 IP 列表     |
|                                                                             |
|  -- 进度消息类型（只读说明）-------------------------------------------- |
|  stageName     stageIndex   触发时机                                        |
|  init          0            IP 列表加载完成                                 |
|  ping          1            延迟测速进行中（节流：每1%或每50个IP）         |
|  ping_done     1            延迟测速完成摘要                                |
|  speed         2            下载测速进行中                                  |
|  speed_done    2            下载测速完成摘要                                |
|  output        3            写 CSV/Hosts/onlyip 完成                       |
|  done          4            本轮全部完成，含完整结果列表                    |
|  error         -1           错误（NO_IPS/CANCELLED/EXCEPTION）             |
|  schedule_wait -1           定时调度等待下次执行                            |
| ---------------------------------------------------------------------------  |
| ---------------------------------------------------------------------------  |
|  JSON 字段参考: stageIndex, totalStages, stageName, ts (Unix秒)             |
|  ping   : done, total, passed, progressPct, passedRate                     |
|  speed  : done, total, progressPct, bestSpeedMbps, latestSpeedMbps         |
|  done   : results[], bestDelayMs, bestSpeedMbps, elapsedMs                 |
|  error  : errorCode (NO_IPS/CANCELLED/EXCEPTION), message                  |
+-----------------------------------------------------------------------------+

### 控件交互逻辑

**调试输出（CheckBox）**
- 绑定 `opts.Debug`；默认 false
- 勾选后 cfst.exe 打印详细内部状态；HTTPing 模式额外输出每个 IP 的状态码和异常信息

**结构化进度输出（CheckBox）**
- 绑定 `opts.ShowProgress`；GUI 模式建议默认勾选
- 勾选时 `CfstProcessManager.OnOutput` 中以 `PROGRESS:` 开头的行被解析为进度事件
- 与静默模式正交：`-silent -progress` 时 stdout 只有 `PROGRESS:` 行和最终 IP 列表

**进度消息说明表（只读，无可编辑控件）**
- 以 Label + DataGrid 或 TextBlock 静态展示各 stageName 说明
- 方便开发者接入时快速查阅字段含义

---

## 页面 8 — 测速结果

对应模块：`ProgressReporter.cs` / `OutputWriter.cs`
对应数据源：`PROGRESS:{...}` 的 `done` 事件 `results[]` 数组 + 本地 `result.csv`

```
+-----------------------------------------------------------------------------+
|  测速结果                                   [↓ 导出 CSV] [✎ 打开文件夹]   |
| ---------------------------------------------------------------------------  |
|  摘要栏（只读）                                                             |
|  本轮共测: 1860 个IP  |  延迟通过: 243  |  速度通过: 38  |  耗时: 02:14   |
|  最低延迟: 48 ms      |  最高速度: 82.63 Mbps                              |
| ---------------------------------------------------------------------------  |
|  序号  IP 地址           丢包率   平均延迟   抖动    下载速度    地区       |
|  ----  ----------------  ------   -------   ------  ----------  ---------  |
|  1     104.19.55.123     0%       48 ms     2.1ms   82.63 Mbps  HKG 香港  |
|  2     104.18.32.45      0%       52 ms     1.8ms   76.20 Mbps  NRT 东京  |
|  3     172.67.12.89      2%       61 ms     3.4ms   65.11 Mbps  LAX 洛杉矶|
|  ...                                                                        |
| ---------------------------------------------------------------------------  |
|  Hosts 更新摘要（仅启用 -hosts 时显示）                                    |
|  已更新: cdn.example.com -> 104.19.55.123                                  |
|  已更新: *.example.com  -> 104.19.55.123  (2 条匹配)                      |
|  [!] 仅预览，未实际写入 (-hosts-dry-run)                                   |
+-----------------------------------------------------------------------------+
```

### 数据来源

| 字段 | 来源 | 更新时机 |
|------|------|----------|
| IP、丢包率、延迟等 | `PROGRESS:done` 事件 `results[]` 数组 | 每轮 `done` 消息触发后整体刷新 |
| 下载速度 | 同上 `results[].speedMbps` | 同上 |
| 地区码 / 地区名 | `results[].colo` + `ColoProvider.GetColoNameZh()` | 同上 |
| 耗时 | `results.elapsedMs` / UI 定时器 | 每秒更新，done 后冻结 |
| Hosts 更新摘要 | `PROGRESS:output` 事件 `hostsUpdated` + GUI 侧记录 | output 消息触发后刷新 |

### 控件交互逻辑

**结果表格（DataGrid / ListView）**
- 数据源绑定到 `TestResult.IpList`（`ObservableCollection<IpResultRow>`）
- `done` 消息到达时清空并重新填充整个列表
- 支持列头单击排序（延迟、速度、丢包率）
- 双击行 → 复制 IP 地址到剪贴板，并弹出 Toast 提示「已复制」

**摘要栏（只读 Label）**
- 绑定 `AppState.SummaryText`，由 `done` 消息的字段计算填充
- 未测速时显示「--」占位

**导出 CSV（Button）**
- 点击打开 SaveFileDialog（*.csv），默认文件名 = `opts.OutputFile`
- 将当前 DataGrid 内容重新写入所选路径（用于更改保存位置）
- 若 CSV 已由 cfst.exe 自动写出，可直接打开目录查看

**打开文件夹（Button）**
- 调用 `Process.Start("explorer.exe", outputDir)` 打开输出目录
- 目录路径 = `opts.OutputDir` 若非空，否则 = `ExePath` 所在目录

**Hosts 更新摘要区（条件显示）**
- 仅当 `opts.HostsDomains` 非空时显示
- 展示每条域名 → IP 的映射关系
- `opts.HostsDryRun == true` 时在标题旁显示橙色「仅预览，未实际写入」徽章

**左侧导航角标更新**
- `done` 消息到达后，导航栏「测速结果」条目角标数字 = `results[].length`
- 点击角标或导航项自动跳转到本页

---

## 全局状态对象 AppState 参考

| 属性 | 类型 | 说明 |
|------|------|------|
| `IsRunning` | `bool` | 测速进行中；控制 Start/Stop 按钮互斥 |
| `Progress` | `int` | 全局进度 0-100，绑定左侧进度条 |
| `StatusText` | `string` | 全局状态文字（就绪/延迟测速中/下载测速中/已完成/已停止）|
| `TestedCount` | `int` | 底部状态栏「已测 N」 |
| `TotalCount` | `int` | 底部状态栏「/Total」 |
| `Elapsed` | `TimeSpan` | 底部耗时，UI 定时器每秒刷新 |
| `BestLatency` | `double` | 底部「当前最快延迟 ms」 |
| `BestSpeed` | `double` | 底部「当前最快速度 Mbps」 |
| `CurrentPage` | `enum` | 当前激活导航页 |
| `SummaryText` | `string` | 结果页摘要栏文字 |

### PROGRESS JSON -> AppState 映射

| stageName | AppState 字段更新 |
|-----------|------------------|
| `init` | `StatusText`="延迟测速中...", `TotalCount`=totalIps |
| `ping` | `Progress`=progressPct, `TestedCount`=done, `StatusText`="延迟测速中..." |
| `ping_done` | `StatusText`="下载测速中..." (若未禁用) 或 "写入结果..." |
| `speed` | `Progress`=progressPct, `BestSpeed`=bestSpeedMbps |
| `speed_done` | `StatusText`="写入结果..." |
| `output` | `StatusText`="已完成" |
| `done` | `IsRunning`=false, `Progress`=100, 刷新结果页, 更新角标 |
| `error` | `IsRunning`=false, `StatusText`="错误: "+errorCode, 恢复按钮 |
| `schedule_wait` | `StatusText`="等待下次执行: "+nextRunTime |

---

## 实现框架选择建议

| 框架 | 推荐场景 | 备注 |
|------|---------|------|
| **WPF** | Windows 桌面，需要丰富动画/样式 | MVVM 模式与 CfstOptions 数据绑定天然契合 |
| **WinForms** | 快速原型、轻量工具 | 控件绑定需手动，事件驱动简单直接 |
| **MAUI** | 跨平台（Windows/macOS/Android/iOS）| 注意移动端 14 节适配项 |
| **Avalonia** | 跨平台桌面（Windows/Linux/macOS）| 接近 WPF 的 XAML 体验，支持 Linux |
