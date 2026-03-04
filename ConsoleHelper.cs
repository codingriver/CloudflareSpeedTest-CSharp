using System.Runtime.InteropServices;

namespace CloudflareST;

/// <summary>
/// Windows 控制台辅助：禁用 QuickEdit 模式，避免点击窗口时进入选择模式导致输出卡住
/// </summary>
internal static class ConsoleHelper
{
    private const int StdInputHandle = -10;
    private const uint ENABLE_QUICK_EDIT = 0x0040;
    private const uint ENABLE_EXTENDED_FLAGS = 0x0080;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    /// <summary>
    /// 禁用 Windows 控制台 QuickEdit 模式。点击窗口时不会进入选择模式，避免输出卡住需按回车才显示。
    /// 非 Windows 或非控制台环境时静默跳过。
    /// </summary>
    public static void DisableQuickEditIfWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            var handle = GetStdHandle(StdInputHandle);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                return;

            if (!GetConsoleMode(handle, out var mode))
                return;

            if ((mode & ENABLE_QUICK_EDIT) == 0)
                return;

            var newMode = (mode & ~ENABLE_QUICK_EDIT) | ENABLE_EXTENDED_FLAGS;
            SetConsoleMode(handle, newMode);
        }
        catch
        {
            // 非控制台、重定向等场景可能失败，忽略
        }
    }

    /// <summary>
    /// 设置 Console.Out 为 AutoFlush，确保每次写入立即刷新到控制台
    /// </summary>
    public static void EnableAutoFlush()
    {
        try
        {
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(stdout);
        }
        catch
        {
            // 重定向等场景可能失败，忽略
        }
    }
}
