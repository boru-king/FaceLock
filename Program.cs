// Program.cs —— 入口：托盘常驻（无常驻窗口）
// 比对时才弹出控制台窗口并打开摄像头，完成后自动关闭/释放。
// 详细比对逻辑见 Comparator.cs，托盘交互见 TrayApp.cs。

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 单实例：防止重复启动导致摄像头争用（TESTMODE 测试构建使用独立命名，便于并行验证）
#if TESTMODE
        using var mutex = new Mutex(true, "FaceLock-testmode", out bool isNew);
#else
        using var mutex = new Mutex(true, "FaceLock-single-instance", out bool isNew);
#endif
        if (!isNew)
        {
            MessageBox.Show("程序已在运行中，请查看任务栏托盘图标。", "FaceLock 人脸锁",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        // 防结束保护：任务管理器"结束任务"将被拒绝（管理员提权仍可结束，作为找回路径）
        AntiKill.Protect();

#if TESTMODE
        // 仅测试构建：150 秒后自动退出，便于自动化验证内存趋势（正式包不含此代码）
        Comparator.Log("TESTMODE：150 秒后自动退出。");
        new System.Threading.Timer(_ => Environment.Exit(0), null, 150_000, Timeout.Infinite);
#endif

        Application.Run(new TrayApp());
    }
}
