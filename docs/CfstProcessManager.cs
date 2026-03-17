// =============================================================================
// docs/CfstProcessManager.cs  -  cfst.exe process lifecycle manager
// =============================================================================
// Responsibility:
//   This class encapsulates the complete lifecycle of a cfst.exe child process:
//     Start (StartAsync) -> listen for output (OnOutput / OnError)
//     -> graceful stop (Stop) / force kill (Kill) -> resource release (Dispose)
//
// Usage scenario:
//   GUI layers (WinForms / WPF / MAUI / Avalonia) use this class to drive
//   cfst.exe as an external process in a "GUI frontend + CLI backend" architecture.
//   For DLL direct-call scenarios (Unity / embedded), use CfstRunner.RunSpeedTestAsync().
//
// Typical usage:
//   var mgr = new CfstProcessManager(@"C:	ools\cfst.exe");
//   mgr.OnOutput += line => Dispatcher.Invoke(() => logBox.AppendText(line + "\n"));
//   mgr.OnError  += line => Dispatcher.Invoke(() => logBox.AppendText("[ERR] " + line + "\n"));
//   mgr.OnExited += code => Dispatcher.Invoke(() => statusLabel.Text = $"Exit: {code}");
//   await mgr.StartAsync(opts, ct);
//
// Thread safety:
//   OnOutput / OnError / OnExited callbacks fire on thread-pool threads.
//   GUI layers must use Dispatcher.Invoke / SynchronizationContext.Post to
//   marshal back to the UI thread before touching controls.
//
// Dependencies:
//   CfstProcessManager -> CfstOptionsExtensions.ToArguments() (arg serialization)
//   CfstProcessManager -> NativeMethods (Windows Ctrl+C graceful stop)
// =============================================================================

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CloudflareST.GUI
{
    /// <summary>
    /// Manages the complete lifecycle of a cfst.exe child process.
    /// <para>Event-driven model: after <see cref="StartAsync"/> the caller receives
    /// process feedback through the <see cref="OnOutput"/>, <see cref="OnError"/>,
    /// <see cref="OnExited"/>, and <see cref="OnStarted"/> events.</para>
    /// <para><b>Difference from CfstRunner:</b> This class runs cfst as an external
    /// process and captures raw stdout/stderr - suitable for GUI isolation.
    /// CfstRunner invokes the test logic in-process (suitable for Unity/DLL).</para>
    /// </summary>
    public sealed class CfstProcessManager : IDisposable
    {
        // ----------------------------------------------------------------
        // Private fields
        // ----------------------------------------------------------------

        /// <summary>The managed child process instance; null when not running.</summary>
        private Process? _process;

        /// <summary>
        /// Linked token source that combines the external cancellation token
        /// with internal cancellation. Cancelled automatically when the caller
        /// triggers the external token, which in turn calls Stop().
        /// </summary>
        private CancellationTokenSource? _cts;

        /// <summary>Guard flag preventing double-dispose.</summary>
        private bool _disposed;

        /// <summary>
        /// Semaphore that enforces single-process constraint: only one
        /// cfst.exe instance may be running at a time through this manager.
        /// </summary>
        private readonly SemaphoreSlim _lock = new(1, 1);

        // ----------------------------------------------------------------
        // Public properties
        // ----------------------------------------------------------------

        /// <summary>
        /// Full path to the cfst executable (absolute or relative).
        /// <para>Example: <c>@"D:\tools\cfst.exe"</c> or <c>"./cfst"</c> (Linux/macOS).</para>
        /// <para>A <see cref="System.IO.FileNotFoundException"/> is thrown by
        /// <see cref="StartAsync"/> if the path does not exist.</para>
        /// </summary>
        public string ExePath { get; set; }

        /// <summary>
        /// Working directory for the child process.
        /// <para>Defaults to null, which resolves to the directory containing
        /// <see cref="ExePath"/>. This ensures relative paths such as
        /// ip.txt, result.csv, and onlyip.txt resolve correctly.</para>
        /// <para>Set an absolute path to redirect output files to a specific directory.</para>
        /// </summary>
        public string? WorkingDirectory { get; set; }

        /// <summary>
        /// Whether the child process is currently running (started and not yet exited).
        /// <para>Thread-safe: reads <see cref="Process.HasExited"/> directly.</para>
        /// </summary>
        public bool IsRunning => _process is { HasExited: false };

        /// <summary>
        /// PID of the current child process, or null when not running.
        /// <para>Useful for log entries and external monitoring tools.</para>
        /// </summary>
        public int? ProcessId
        {
            get
            {
                try { return IsRunning ? _process!.Id : null; }
                catch { return null; } // process already disposed between check and read
            }
        }

        // ----------------------------------------------------------------
        // Events
        // ----------------------------------------------------------------

        /// <summary>
        /// Fired when a stdout line is received (on a thread-pool thread).
        /// <para>When cfst.exe is started with the <c>-progress</c> flag, lines prefixed
        /// with <c>PROGRESS:</c> carry structured JSON progress events. Recommended
        /// handling in the GUI subscriber:</para>
        /// <list type="bullet">
        ///   <item>Lines starting with <c>PROGRESS:</c> -> strip prefix, parse JSON,
        ///     drive progress bar and status label.</item>
        ///   <item>All other lines -> append to the run-log text box.</item>
        /// </list>
        /// <para><b>Progress stageName values:</b>
        /// init / ping / ping_done / speed / speed_done / output / done / error / schedule_wait</para>
        /// <para><b>Thread note:</b> This event fires on a thread-pool thread.
        /// WPF/WinForms subscribers must marshal to the UI thread via
        /// Dispatcher.Invoke or SynchronizationContext.Post before touching controls.</para>
        /// </summary>
        public event Action<string>? OnOutput;

        /// <summary>
        /// Fired when a stderr line is received (on a thread-pool thread).
        /// <para>cfst.exe writes internal exceptions and fatal errors to stderr.
        /// GUI subscribers typically display these as red error log entries.</para>
        /// <para><b>Thread note:</b> Same thread-pool caveat as <see cref="OnOutput"/>.</para>
        /// </summary>
        public event Action<string>? OnError;

        /// <summary>
        /// Fired when the process exits normally or is terminated. Argument is the exit code.
        /// <para><b>Exit code semantics:</b> 0 = success; 1 = failure
        /// (no available IPs, user cancellation, or unhandled exception).</para>
        /// <para>GUI subscribers typically restore the Start button, update the
        /// status bar to "Completed" or "Stopped", and refresh the results page.</para>
        /// </summary>
        public event Action<int>? OnExited;

        /// <summary>
        /// Fired immediately after the process is successfully started
        /// (called on the <see cref="StartAsync"/> caller's thread).
        /// <para>GUI subscribers typically disable the Start button, enable the Stop
        /// button, and start an elapsed-time timer here.</para>
        /// </summary>
        public event Action? OnStarted;

        // ----------------------------------------------------------------
        // Constructor
        // ----------------------------------------------------------------

        /// <summary>
        /// Create a new process manager.
        /// </summary>
        /// <param name="exePath">
        /// Full path to the cfst executable, e.g. <c>@"D:\tools\cfst.exe"</c>.
        /// If the path does not exist, <see cref="StartAsync"/> will throw
        /// <see cref="System.IO.FileNotFoundException"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when exePath is null.</exception>
        public CfstProcessManager(string exePath)
        {
            ExePath = exePath ?? throw new ArgumentNullException(nameof(exePath));
        }

        // ----------------------------------------------------------------
        // Public methods
        // ----------------------------------------------------------------

        /// <summary>
        /// Asynchronously start the cfst.exe child process.
        /// <para><b>Internal sequence:</b></para>
        /// <list type="number">
        ///   <item>Validate: not already running, ExePath exists.</item>
        ///   <item>Serialize options via <see cref="CfstOptionsExtensions.ToArguments"/>.</item>
        ///   <item>Configure ProcessStartInfo (redirect I/O, UTF-8, no console window).</item>
        ///   <item>Register external CancellationToken to call Stop() on cancellation.</item>
        ///   <item>Start process, begin async output reading, fire <see cref="OnStarted"/>.</item>
        /// </list>
        /// </summary>
        /// <param name="options">
        /// Speed-test options. Pass null to run with all defaults
        /// (equivalent to launching cfst.exe with no arguments).
        /// </param>
        /// <param name="cancellationToken">
        /// External cancellation token. When triggered, automatically calls
        /// <see cref="Stop"/> and waits for graceful exit before forcing Kill.
        /// </param>
        /// <exception cref="InvalidOperationException">Thrown when a process is already running.</exception>
        /// <exception cref="System.IO.FileNotFoundException">Thrown when ExePath does not exist.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
        public Task StartAsync(CfstOptions? options = null,
                               CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().FullName);

            if (IsRunning)
                throw new InvalidOperationException(
                    "cfst process is already running. Call Stop() or Kill() and wait before starting again.");

            if (!System.IO.File.Exists(ExePath))
                throw new System.IO.FileNotFoundException(
                    $"cfst executable not found: {ExePath}", ExePath);

            // Serialize CfstOptions -> argument string; null options = empty string = all defaults
            var arguments = options?.ToArguments() ?? string.Empty;

            // Working directory: explicit setting takes priority; otherwise use ExePath's directory
            // so relative paths (ip.txt, result.csv, etc.) resolve correctly.
            var workDir = WorkingDirectory
                          ?? System.IO.Path.GetDirectoryName(ExePath)
                          ?? ".";

            var psi = new ProcessStartInfo
            {
                FileName               = ExePath,
                Arguments              = arguments,
                WorkingDirectory       = workDir,
                UseShellExecute        = false,  // required for I/O redirection
                RedirectStandardOutput = true,   // capture stdout (progress / results / log)
                RedirectStandardError  = true,   // capture stderr (internal exceptions)
                RedirectStandardInput  = false,  // no need to write to process stdin
                CreateNoWindow         = true,   // run headless - no black console window
                StandardOutputEncoding = System.Text.Encoding.UTF8,  // cfst outputs UTF-8
                StandardErrorEncoding  = System.Text.Encoding.UTF8,
            };

            // Link external token: when caller cancels, automatically invoke Stop()
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            // Register async stdout handler: fires once per complete output line
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null) OnOutput?.Invoke(e.Data);
            };

            // Register async stderr handler: fires once per complete error line
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) OnError?.Invoke(e.Data);
            };

            // Register process-exit handler: pass exit code to subscribers
            _process.Exited += (_, _) =>
            {
                int code = TryGetExitCode();
                OnExited?.Invoke(code);
            };

            // Register cancellation -> Stop() bridge
            _cts.Token.Register(() => Stop());

            _process.Start();
            _process.BeginOutputReadLine();  // start async stdout pump
            _process.BeginErrorReadLine();   // start async stderr pump

            // Notify GUI that process has started (PID is now readable)
            OnStarted?.Invoke();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Wait for the process to exit, with an optional timeout.
        /// </summary>
        /// <param name="timeoutMs">
        /// Timeout in milliseconds. -1 = wait indefinitely; 0 = poll without blocking.
        /// </param>
        /// <returns>
        /// <c>true</c> if the process exited within the timeout;
        /// <c>false</c> if still running when the timeout elapsed.
        /// </returns>
        public async Task<bool> WaitForExitAsync(int timeoutMs = -1)
        {
            if (_process is null || _process.HasExited) return true;

            if (timeoutMs < 0)
            {
                // Indefinite wait - no cancellation needed
                await _process.WaitForExitAsync();
                return true;
            }

            // Timed wait: use a CancellationTokenSource as the timeout mechanism
            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await _process.WaitForExitAsync(cts.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                // Timeout elapsed - process is still running
                return false;
            }
        }

        /// <summary>
        /// Gracefully stop the process: send Ctrl+C (Windows only), then wait
        /// up to <paramref name="gracePeriodMs"/> milliseconds for the process to
        /// exit on its own before force-killing it.
        /// </summary>
        /// <param name="gracePeriodMs">
        /// Milliseconds to wait for graceful exit. Default: 3000 ms.
        /// cfst.exe finishes the current test round and writes output files on
        /// Ctrl+C; it typically exits within 1 second.
        /// </param>
        /// <remarks>
        /// On Linux/macOS the Ctrl+C P/Invoke is not available; the method waits
        /// gracePeriodMs then kills. For cross-platform graceful stop, cfst would
        /// need to handle SIGTERM (currently only SIGINT / Ctrl+C is handled).
        /// </remarks>
        public void Stop(int gracePeriodMs = 3000)
        {
            if (_process is null || _process.HasExited) return;

            try
            {
                // Windows: send Ctrl+C via P/Invoke so cfst.exe Console.CancelKeyPress
                // handler can flush output and exit cleanly.
                if (System.Runtime.InteropServices.RuntimeInformation
                        .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    try { NativeMethods.SendCtrlC(_process.Id); }
                    catch { /* Ignore send failure; will fall through to Kill */ }
                }

                // Wait for the process to exit within the grace period
                bool exited = _process.WaitForExit(gracePeriodMs);
                if (!exited)
                {
                    // Grace period elapsed - force terminate
                    Kill();
                }
            }
            catch (InvalidOperationException)
            {
                // Process exited during the wait - nothing to do
            }
        }

        /// <summary>
        /// Immediately force-terminate the process
        /// (Windows: TerminateProcess; Linux/macOS: SIGKILL).
        /// </summary>
        /// <param name="killTree">
        /// <c>true</c> (default) = also kill all child processes spawned by cfst.exe,
        /// preventing orphan processes. <c>false</c> = kill only the direct child.
        /// </param>
        /// <remarks>
        /// Does not wait for cleanup. Output files may be incomplete.
        /// Use only after <see cref="Stop"/> grace period has expired or in emergency.
        /// </remarks>
        public void Kill(bool killTree = true)
        {
            if (_process is null || _process.HasExited) return;

            try
            {
                _process.Kill(entireProcessTree: killTree);
            }
            catch (InvalidOperationException)
            {
                // Process exited between the HasExited check and Kill() - ignore
            }
            catch (Exception ex)
            {
                // Log but do not rethrow; edge cases include insufficient permissions
                System.Diagnostics.Debug.WriteLine(
                    $"[CfstProcessManager] Kill failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Cancel the internal token, which triggers the Stop() flow.
        /// <para>Provides a semantically clearer alternative to calling
        /// <see cref="Stop"/> directly from the GUI "Stop" button handler.</para>
        /// </summary>
        public void Cancel() => _cts?.Cancel();

        // ----------------------------------------------------------------
        // Private helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Safely read the process exit code.
        /// Returns -1 if the process object has already been disposed.
        /// </summary>
        private int TryGetExitCode()
        {
            try { return _process?.ExitCode ?? -1; }
            catch { return -1; }
        }

        // ----------------------------------------------------------------
        // IDisposable
        // ----------------------------------------------------------------

        /// <summary>
        /// Release all resources: force-terminate the child process, dispose
        /// the cancellation token source and process object.
        /// <para>Multiple calls to Dispose() are safe (idempotent).</para>
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Kill();  // ensure process is terminated before releasing handles

            _cts?.Dispose();
            _cts = null;

            _process?.Dispose();
            _process = null;

            _lock.Dispose();
        }
    }

    // =========================================================================
    // NativeMethods  -  Windows Ctrl+C signal helper
    // =========================================================================

    /// <summary>
    /// Sends a Ctrl+C console control event to a target process via Win32 APIs.
    /// This allows cfst.exe to handle the signal through its
    /// Console.CancelKeyPress handler, performing a clean shutdown
    /// (finish current test, flush output files, then exit).
    /// <para><b>Windows only.</b> Not called on Linux/macOS.</para>
    /// <para><b>Mechanism:</b></para>
    /// <list type="number">
    ///   <item>FreeConsole() - detach this process from its current console.</item>
    ///   <item>AttachConsole(pid) - attach to the target process's console.</item>
    ///   <item>GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0) - broadcast Ctrl+C to all
    ///     processes attached to that console.</item>
    ///   <item>FreeConsole() - detach again to restore state.</item>
    /// </list>
    /// <para><b>Caution:</b> Briefly alters the calling process's console attachment.
    /// Not thread-safe if multiple threads call SendCtrlC concurrently.</para>
    /// </summary>
    internal static class NativeMethods
    {
        /// <summary>Attach the calling process to the console of the specified process.</summary>
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        /// <summary>Detach the calling process from its current console.</summary>
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        /// <summary>
        /// Send a control event to all processes attached to the current console.
        /// <para>dwProcessGroupId = 0 broadcasts to all attached processes.</para>
        /// </summary>
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool GenerateConsoleCtrlEvent(
            uint dwCtrlEvent, uint dwProcessGroupId);

        /// <summary>CTRL_C_EVENT value (0): corresponds to keyboard Ctrl+C.</summary>
        private const uint CTRL_C_EVENT = 0;

        /// <summary>
        /// Send a Ctrl+C signal to the process with the given PID,
        /// causing it to fire its Console.CancelKeyPress handler.
        /// </summary>
        /// <param name="pid">Target process ID (from Process.Id).</param>
        internal static void SendCtrlC(int pid)
        {
            FreeConsole();                    // detach from current console
            if (AttachConsole((uint)pid))     // attach to target's console
            {
                GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0);  // broadcast Ctrl+C
                FreeConsole();                // detach again to restore state
            }
        }
    }
}
