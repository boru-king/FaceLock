// Native.cs —— Win32 P/Invoke 声明
using System.Runtime.InteropServices;

internal static class Native
{
    /// <summary>锁定 Windows 工作站（比对失败时调用）。</summary>
    [DllImport("user32")]
    internal static extern bool LockWorkStation();

    /// <summary>为进程分配一个新的控制台窗口（比对开始时弹出）。</summary>
    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool AllocConsole();

    /// <summary>释放控制台窗口（比对完成后关闭）。</summary>
    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool FreeConsole();

    /// <summary>设置控制台窗口标题。</summary>
    [DllImport("kernel32")]
    internal static extern bool SetConsoleTitle(string lpConsoleTitle);

    /// <summary>获取当前进程关联的控制台窗口句柄。</summary>
    [DllImport("kernel32")]
    internal static extern IntPtr GetConsoleWindow();

    /// <summary>获取窗口系统菜单（用于禁用关闭按钮）。</summary>
    [DllImport("user32")]
    internal static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

    /// <summary>启用/禁用系统菜单项。</summary>
    [DllImport("user32")]
    internal static extern bool EnableMenuItem(IntPtr hMenu, uint uIDEnableItem, uint uEnable);

    /// <summary>设置内核对象（进程）的安全描述符——用于给自己加"防结束"拒绝 ACE。</summary>
    [DllImport("advapi32", SetLastError = true)]
    internal static extern bool SetKernelObjectSecurity(IntPtr handle, int securityInformation, byte[] securityDescriptor);
}
