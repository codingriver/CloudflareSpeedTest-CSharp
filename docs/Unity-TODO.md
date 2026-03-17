# Unity 兼容性 TODO 列表

> 已完成项标记为 ✅。本文件列出所有改动及当前状态。

---

## 1. `Environment.Exit()` 调用移除 ✅

**文件**：`Program.cs`  
**状态**：`Program.cs` 整体被 `#if !UNITY_BUILD` 包裹，Unity 编译时完全跳过。

---

## 2. `Console.CancelKeyPress` 仅限 CLI ✅

**文件**：`Core/CfstRunner.cs`  
**状态**：Unity 直接调用 `RunSpeedTestAsync`，不经过 `RunCliAsync`，信号注册不会被触发。

---

## 3. `Console.WriteLine` 输出适配 Unity ✅

**文件**：`Core/CfstRunner.cs`  
**状态**：已完成。
- `CfstRunner.LogHandler`：`public static Action<string>? LogHandler { get; set; }`
- 所有 `Console.WriteLine` / `Console.Write` 已替换为 `Log()` / `LogInline()`
- Unity 侧：`CfstRunner.LogHandler = UnityEngine.Debug.Log;`

---

## 4. `ProgressReporter.cs` — IL2CPP 反射兼容 ✅

**文件**：`Core/ProgressReporter.cs`  
**状态**：已完成。移除 `System.Text.Json` 匿名类型序列化，改为手动拼 JSON 字符串，完全无反射。

---

## 5. `.NET` 版本目标框架配置 ✅

**文件**：`CloudflareST.csproj`  
**状态**：已完成。
- `<TargetFrameworks>net8.0;netstandard2.1</TargetFrameworks>`
- `netstandard2.1` 自动定义 `UNITY_BUILD`，`IsPublishable=false`
- AOT/Trim publish 时自动收窄为 `net8.0`，消除 NETSDK1207/NETSDK1124
- 构建命令：`build.ps1 -unity`

---

## 6. `Process.WaitForExitAsync` 兼容性

**文件**：`docs/CfstProcessManager.cs`（仅参考，不参与编译）  
**状态**：待定——Unity 直接调用 DLL 时无需进程管理。若需移植参考：
```csharp
var tcs = new TaskCompletionSource<bool>();
process.Exited += (_, _) => tcs.TrySetResult(true);
await tcs.Task;
```

---

## 7. `Cronos` 第三方库 Unity 兼容验证

**状态**：未验证。`Cronos 0.8.4` 支持 `netstandard2.0`，理论兼容。  
**待做**：
- [ ] 在 Unity 中测试 `-cron` 功能
- [ ] 若 IL2CPP 裁剪崩溃，在 `link.xml` 添加保留规则

---

## 8. 多次调用线程安全（重入保护） ✅

**文件**：`Core/CfstRunner.cs`、`Core/ProgressReporter.cs`  
**状态**：已完成。
- `ProgressReporter._lastPingReportDone` 改为 `Interlocked.Exchange` 操作，消除并发竞态
- `CfstRunner.IsRunning`：`public static bool IsRunning => _runCts != null;`
- `CfstRunner.Stop()`：取消当前运行任务，`_runCts?.Cancel()`
- `RunSpeedTestAsync` 内部加 `lock(_runLock)` 重入检测，并发调用时抛 `InvalidOperationException`
- `finally` 块保证任务结束后自动释放 `_runCts`

---

## 9. Unity `.asmdef` 配置（源码方案）

**状态**：DLL 方案已就绪，源码方案按需实施。

---

## 10. 文件级 namespace 改回传统写法 ✅

**状态**：已完成。Core 目录全部 16 个文件已改回 `namespace CloudflareST { }` 花括号写法。

---

## 11. Program.cs 顶级语句 ✅

**状态**：已完成。整体被 `#if !UNITY_BUILD` 包裹，无需 `.asmdef` 排除。

---

## 12. `System.Threading.Channels` 依赖移除 ✅

**文件**：`Core/PingTester.cs`、`Core/IcmpPinger.cs`、`Core/HttpingTester.cs`、`Core/SpeedTester.cs`  
**状态**：已完成。4 个文件中 `Channel<T>` 全部替换为 `ConcurrentQueue<T>.TryDequeue()`，NuGet 包已移除。  
Unity 不再出现 "Unable to resolve reference 'System.Threading.Channels'" 错误。

---

## 13. 结构化进度回调 `ProgressHandler` ✅

**文件**：`Core/CfstRunner.cs`、`Core/ProgressReporter.cs`  
**状态**：已完成。
- `CfstRunner.ProgressHandler`：`public static Action<string>? ProgressHandler { get; set; }`
- `Emit()` 同时调用 `CfstRunner.ProgressHandler?.Invoke(json)`
- Unity 侧直接获取纯 JSON，无需解析 `PROGRESS:` 前缀

**Unity 侧使用：**
```csharp
CfstRunner.ProgressHandler = (json) => {
    // json: {"stageName":"ping","done":50,...}
};
var config = new Config { ShowProgress = true };
var results = await CfstRunner.RunSpeedTestAsync(config);
```

---

## Unity 接入快速指南

### DLL 方式（推荐）

1. 运行 `build.ps1 -unity`，获取 `publish/cfst.dll` + `publish/Cronos.dll`
2. 复制到 Unity 项目 `Assets/Plugins/CFST/`
3. Player Settings → Scripting Define Symbols 加入 `UNITY_BUILD`
4. 注入回调并调用：
```csharp
CfstRunner.LogHandler = UnityEngine.Debug.Log;
CfstRunner.ProgressHandler = (json) => { /* 解析进度 */ };
var results = await CfstRunner.RunSpeedTestAsync(new Config { ShowProgress = true });
```

### 进度消息 stageName 枚举

| stageName | 触发时机 | 关键字段 |
|-----------|---------|----------|
| `init` | IP 列表加载完成 | `totalIps`, `pingMode` |
| `ping` | 延迟测速进行中 | `done`, `total`, `passed`, `progressPct` |
| `ping_done` | 延迟测速完成 | `passed`, `filtered`, `passedRate` |
| `speed` | 下载测速进行中 | `done`, `total`, `bestSpeedMbps` |
| `speed_done` | 下载测速完成 | `bestSpeedMbps`, `avgSpeedMbps` |
| `output` | 写文件完成 | `outputCount`, `hostsUpdated` |
| `done` | 本轮全部完成 | `results[]`, `bestDelayMs`, `elapsedMs` |
| `error` | 发生错误 | `errorCode`, `message` |
| `schedule_wait` | 等待下次调度 | `nextRunTime` |

### 优先级（当前状态）

| 优先级 | 编号 | 状态 |
|--------|------|------|
| ✅ | 3,4,5,8,10,11,12,13 | 已完成 |
| 🟢 按需 | 7 | Cronos IL2CPP 验证 |
| 🟢 按需 | 6 | 进程管理，外部进程方案才需要 |
| 🟢 按需 | 9 | asmdef，源码方案才需要 |
---

## 14. 移动平台（Android / iOS）适配

> **前提**：移动平台使用 **DLL 直接调用方案**（`CfstRunner.RunSpeedTestAsync`）。`System.Diagnostics.Process` 在 Android/iOS 上完全不可用，不使用 `CfstProcessManager`。

### 14-A. 文件 I/O 路径适配

**涉及文件**：无需修改 DLL  
**问题**：默认相对路径 result.csv/ip.txt 在移动端无写权限。  
**改造方案**：调用时配置绝对路径，无需修改源码：

    var d = Application.persistentDataPath;
    var config = new Config {
        IpFiles    = new List<string> { Path.Combine(d, "ip.txt") },
        OutputFile = Path.Combine(d, "result.csv"),
        OnlyIpFile = Path.Combine(d, "onlyip.txt"),
    };

ip.txt 需预先写入 persistentDataPath（从 StreamingAssets 拷贝或网络下载）。  
**改动量**：无需修改 DLL ✅

### 14-B. HostsUpdater — 移动端无 hosts 写权限

**涉及文件**：无需修改 DLL  
**问题**：Android/iOS 无法修改系统 hosts。  
**改造方案**：不设置 Config.HostEntries，现有保护直接跳过。  
**改动量**：无需修改 DLL ✅

### 14-C. ConsoleHelper — 移动端无控制台

**涉及文件**：Core/ConsoleHelper.cs  
**问题**：EnableAutoFlush() 和 P/Invoke 在移动端可能异常。  
**改造方案**：已有 try/catch 保护，无需操作。  
**改动量**：无需修改 DLL ✅

### 14-D. IcmpPinger — Android 非 root / iOS 无原始 Socket 权限

**涉及文件**：无需修改 DLL  
**问题**：ICMP 在移动端不可用。  
**改造方案**：预检失败自动降级 TCPing（已有逻辑）。建议移动端直接配置：

    #if UNITY_ANDROID || UNITY_IOS
    config.TcpPingMode = true;
    #endif

**改动量**：无需修改 DLL ✅

### 14-E. HttpClientHandler SSL 证书验证（可选，安全加固）

**涉及文件**：Core/HttpingTester.cs、Core/SpeedTester.cs  
**问题**：#if UNITY_BUILD 分支跳过 SSL 验证，存在中间人攻击风险。  
**改造方案**（如需加固）：
- [ ] 将 ServerCertificateCustomValidationCallback 改为仅信任 Cloudflare 证书指纹或系统信任链

**改动量**：2 个文件少量修改 🟢

### 14-F. ProgressHandler / LogHandler 回调线程切换

**涉及文件**：Unity 侧代码（不在本 DLL 中）  
**问题**：回调在线程池线程上触发，Unity UI 操作必须在主线程。  
**改造方案**：

    // 方案 A: UniTask
    CfstRunner.ProgressHandler = async (json) => {
        await UniTask.SwitchToMainThread();
        UpdateUI(json);
    };
    // 方案 B: ConcurrentQueue + Update() 轮询
    private ConcurrentQueue<string> _q = new();
    void Start() { CfstRunner.ProgressHandler = json => _q.Enqueue(json); }
    void Update() { while (_q.TryDequeue(out var json)) UpdateUI(json); }

**改动量**：仅 Unity 侧，不修改 DLL ✅

### 14-G. Scheduler — 移动端后台调度限制

**涉及文件**：无需修改 DLL  
**问题**：iOS/Android 应用切后台被系统挂起，定时调度中断。  
**改造方案**：移动端不使用 -interval/-cron，单次调用后处理结果。如需后台定时，由平台原生能力（iOS BGTaskScheduler / Android WorkManager）触发。  
**改动量**：无需修改 DLL ✅

### 14-H. OutputWriter CSV 输出 — 移动端可跳过

**涉及文件**：Core/OutputWriter.cs、Core/CfstRunner.cs  
**问题**：移动端通常直接用返回的 IReadOnlyList<IPInfo> 结果，不需要 CSV。  
**改造方案**：
- [ ] 如需完全跳过文件输出，需在 OutputWriter.ExportCsvAsync 和 CfstRunner 中对空路径做保护（当前空路径会抛异常）
- [ ] 或设置 Config.DisableOutput = true 标志（需修改 Config 和 CfstRunner）

**改动量**：按需，目前配置 14-A 路径即可正常输出 🟢

### 移动平台改动量汇总

| 编号 | 项目 | 是否需要修改 DLL | 优先级 |
|------|------|----------------|----------|
| 14-A | 文件路径 | 无，Unity 侧配置 | 🔴 必须 |
| 14-B | HostsUpdater | 无，不设置即可 | ✅ 自动 |
| 14-C | ConsoleHelper | 无，已有保护 | ✅ 自动 |
| 14-D | IcmpPinger | 无，Unity 侧配置 | 🔴 必须 |
| 14-E | SSL 验证 | 可选，2 个文件 | 🟢 按需 |
| 14-F | 回调线程 | 无，Unity 侧处理 | 🔴 必须 |
| 14-G | Scheduler | 无，不使用即可 | ✅ 自动 |
| 14-H | CSV 输出 | 按需，少量修改 | 🟢 按需 |

---

## 15. GUI 布局设计文档 ✅

**文件**：`docs/GUI-Layout-Design.md`  
**状态**：已完成。全 8 个导航页 + 全局 AppState 参考表均已设计完毕。

### 页面覆盖清单

| 页面 | 主要绑定模块 | 状态 |
|------|------------|------|
| 1. IP 来源 | `IpProvider` / `Config` | ✅ |
| 2. 延迟测速 | `IcmpPinger` / `PingTester` / `HttpingTester` | ✅ |
| 3. 下载测速 | `SpeedTester` | ✅ |
| 4. 定时调度 | `Scheduler` | ✅ |
| 5. Hosts 更新 | `HostsUpdater` | ✅ |
| 6. 输出设置 | `OutputWriter` / `ConsoleHelper` | ✅ |
| 7. 其他设置 | `ProgressReporter`（进度消息说明表）| ✅ |
| 8. 测速结果 | `PROGRESS:done` / `OutputWriter` | ✅ |

### AppState 关键属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `IsRunning` | `bool` | 控制 Start/Stop 按钮互斥 |
| `Progress` | `int` | 全局进度 0–100，绑定进度条 |
| `StatusText` | `string` | 就绪/延迟测速中/下载测速中/已完成/已停止 |
| `TestedCount` / `TotalCount` | `int` | 底部状态栏「已测 N/Total」|
| `BestLatency` / `BestSpeed` | `double` | 底部「当前最快」|
| `CurrentPage` | `enum` | 当前激活导航页 |
| `SummaryText` | `string` | 结果页摘要栏文字 |

### 数据流（外进程方案）

```
GUI 控件 <--双向绑定--> CfstOptions
    └── ToArguments() --> CfstProcessManager.StartAsync()
              └── stdout OnOutput 回调
                    ├── PROGRESS:{json} --> AppState / 进度条
                    └── 其余行 --> 运行日志
```

### 数据流（Unity DLL 直调方案）

```
GUI 控件 <--双向绑定--> Config
    └── CfstRunner.RunSpeedTestAsync(config)
          ├── CfstRunner.ProgressHandler --> AppState / 进度条
          └── CfstRunner.LogHandler     --> 运行日志
```

