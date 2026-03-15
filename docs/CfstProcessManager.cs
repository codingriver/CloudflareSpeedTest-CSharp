// ============================================================
// CfstProcessManager.cs
// 管理 cfst 可执行文件的启动、输出监听、停止和强制终止
// ============================================================

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CloudflareST.GUI;

/// <summary>
/// 管理 cfst 可执行文件进程的完整生命周期：
/// 启动 (StartAsync) → 监听输出 (OnOutput/OnError) → 优雅停止 (Stop) / 强制终止 (Kill)
/// </summary>public sealed class CfstProcessManager : IDisposable
{
    // ── 私有字段 ──────────────────────────────────────────────
    private Process?                  _process;
    private CancellationTokenSource? _cts;
    private bool                      _disposed;
    private readonly SemaphoreSlim    _lock = new(1, 1);

    // ── 公开属性 ──────────────────────────────────────────────

    /// <summary>cfst 可执行文件的完整路径</summary>
    public string ExePath { get; set; }

    /// <summary>工作目录，默认为 ExePath 所在目录</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>进程是否正在运行（未退出）</summary>
    public bool IsRunning => _process is { HasExited: false };

    /// <summary>当前进程 ID，未启动或已退出时为 null</summary>
    public int? ProcessId
    {
        get
        {
            try { return IsRunning ? _process!.Id : null; }
            catch { return null; }
        }
    }

    // ── 事件 ─────────────────────────────────────────────────

    /// <summary>
    /// 收到标准输出行时触发（在线程池线程上调用）。
    /// 当 cfst 以 -progress 参数启动时，以 "PROGRESS:" 开头的行为结构化进度消息。
    /// GUI 层建议在此事件中按前缀分流处理：
    ///   if (line.StartsWith("PROGRESS:"))
    ///       // 解析进度 JSON，驱动进度条 / 状态栏
    ///   else
    ///       // 追加到运行日志文本框
    /// 进度消息 stageName 枚举：
    ///   init / ping / ping_done / speed / speed_done / output / done / error / schedule_wait
    /// 公共字段：stageIndex, totalStages, stageName, ts（Unix 秒）
    /// </summary>
    public event Action<string>? OnOutput;

    /// <summary>收到标准错误行时触发（在线程池线程上调用）</summary>
    public event Action<string>? OnError;

    /// <summary>进程正常退出或被终止后触发，参数为退出码</summary>
    public event Action<int>? OnExited;

    /// <summary>进程启动成功后触发</summary>
    public event Action? OnStarted;

    // ── 构造函数 ──────────────────────────────────────────────

    /// <param name="exePath">cfst 可执行文件完整路径，例如 @"D:\tools\cfst.exe"</param>
    public CfstProcessManager(string exePath)
    {
        ExePath = exePath ?? throw new ArgumentNullException(nameof(exePath));
    }

    // ── 公开方法 ──────────────────────────────────────────────

    /// <summary>
    /// 异步启动 cfst 进程。
    /// </summary>
    /// <param name="options">测速参数；为 null 时使用全默认参数运行</param>
    /// <param name="cancellationToken">外部取消令牌，取消时自动调用 Stop()</param>
    /// <exception cref="InvalidOperationException">进程已在运行时抛出</exception>
    /// <exception cref="System.IO.FileNotFoundException">ExePath 不存在时抛出</exception>
    public Task StartAsync(CfstOptions?      options           = null,
                           CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsRunning)
            throw new InvalidOperationException(
                "cfst 进程已在运行，请先调用 Stop() 或 Kill()。");

        if (!System.IO.File.Exists(ExePath))
            throw new System.IO.FileNotFoundException(
                $"找不到 cfst 可执行文件：{ExePath}", ExePath);

        var arguments = options?.ToArguments() ?? string.Empty;
        var workDir   = WorkingDirectory
                        ?? System.IO.Path.GetDirectoryName(ExePath)
                        ?? ".";

        var psi = new ProcessStartInfo
        {
            FileName               = ExePath,
            Arguments              = arguments,
            WorkingDirectory       = workDir,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            RedirectStandardInput  = false,
            CreateNoWindow         = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding  = System.Text.Encoding.UTF8,
        };

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) OnOutput?.Invoke(e.Data);
        };

        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) OnError?.Invoke(e.Data);
        };

        _process.Exited += (_, _) =>
        {
            int code = TryGetExitCode();
            OnExited?.Invoke(code);
        };

        _cts.Token.Register(() => Stop());

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        OnStarted?.Invoke();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 等待进程退出，可指定超时。
    /// </summary>
    /// <param name="timeoutMs">超时毫秒数，-1 表示无限等待</param>
    /// <returns>true = 进程已退出；false = 超时仍在运行</returns>
    public async Task<bool> WaitForExitAsync(int timeoutMs = -1)
    {
        if (_process is null || _process.HasExited) return true;

        if (timeoutMs < 0)
        {
            await _process.WaitForExitAsync();
            return true;
        }

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await _process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// 优雅停止进程：先发送 Ctrl+C 信号（仅 Windows），
    /// 等待 <paramref name="gracePeriodMs"> 毫秒后若仍未退出则强制 Kill。
    /// </summary>
    /// <param name="gracePeriodMs">等待优雅退出的毫秒数，默认 3000</param>
    public void Stop(int gracePeriodMs = 3000)
    {
        if (_process is null || _process.HasExited) return;

        try
        {
            // Windows：尝试发送 Ctrl+C 让程序自行收尾
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    // AttachConsole + GenerateConsoleCtrlEvent 发送 Ctrl+C
                    NativeMethods.SendCtrlC(_process.Id);
                }
                catch
                {
                    // 发送失败时直接 Kill，不影响后续逻辑
                }
            }

            // 等待进程自行退出
            bool exited = _process.WaitForExit(gracePeriodMs);
            if (!exited)
            {
                Kill();
            }
        }
        catch (InvalidOperationException)
        {
            // 进程已退出，忽略
        }
    }

    /// <summary>
    /// 强制立即终止进程（SIGKILL / TerminateProcess）。
    /// 不等待进程自行清理，数据可能丢失，但保证立即结束。
    /// </summary>
    /// <param name="killTree">true = 同时终止子进程树（默认 true）</param>
    public void Kill(bool killTree = true)
    {
        if (_process is null || _process.HasExited) return;

        try
        {
            _process.Kill(entireProcessTree: killTree);
        }
        catch (InvalidOperationException)
        {
            // 进程已退出，忽略
        }
        catch (Exception ex)
        {
            // 其他平台权限问题等，记录但不抛出
            System.Diagnostics.Debug.WriteLine($"[CfstProcessManager] Kill 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 取消外部 CancellationToken 关联的令牌源，触发 Stop()。
    /// GUI 层可用此方法代替直接调用 Stop()。
    /// </summary>
    public void Cancel() => _cts?.Cancel();

    // ── 私有辅助 ──────────────────────────────────────────────

    private int TryGetExitCode()
    {
        try { return _process?.ExitCode ?? -1; }
        catch { return -1; }
    }

    // ── IDisposable ───────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Kill();

        _cts?.Dispose();
        _cts = null;

        _process?.Dispose();
        _process = null;

        _lock.Dispose();
    }
}

// ── Windows 原生 Ctrl+C 辅助 ──────────────────────────────────

/// <summary>
/// 向目标进程发送 Ctrl+C 控制台信号（仅 Windows）。
/// </summary>
internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    private const uint CTRL_C_EVENT = 0;

    /// <summary>向指定 PID 的进程发送 Ctrl+C 信号</summary>
    internal static void SendCtrlC(int pid)
    {
        FreeConsole();
        if (AttachConsole((uint)pid))
        {
            GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0);
            FreeConsole();
        }
    }
}
