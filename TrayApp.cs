// TrayApp.cs —— 任务栏托盘常驻：图标、右键菜单、定时调度、气泡提示
// 锁屏感知：锁屏（自动或手动）时暂停比对与摄像头检测，解锁后自动恢复。
using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly Comparator _comparator = new();
    private System.Threading.Timer? _timer;
    private int _running;            // 0=空闲 1=比对进行中（防重入）
    private volatile bool _paused;   // 锁屏期间暂停检测
    private System.Threading.Timer? _unlockCheckTimer;   // 持有引用防被 GC（一次性补比对）

    internal TrayApp()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("立即比对一次", null, (_, _) => RunNow());
        menu.Items.Add("设置...", null, (_, _) =>
        {
            if (RequirePassword("修改设置")) OpenSettings();
        });
        menu.Items.Add("修改密码...", null, (_, _) => ChangePassword());
        menu.Items.Add("打开日志", null, (_, _) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) =>
        {
            if (RequirePassword("退出程序")) ExitApp();
        });

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = $"FaceLock 人脸锁（每 {Settings.Current.IntervalText}）",
            ContextMenuStrip = menu,
            Visible = true,
        };
        string tip = PasswordStore.IsSet
            ? "FaceLock 已启动，模型加载后开始比对。退出/设置需密码。"
            : "FaceLock 已启动，模型加载后开始比对。退出/设置首次使用时会提示设置密码。";
        _icon.ShowBalloonTip(3000, "FaceLock 人脸锁", tip, ToolTipIcon.Info);

        // 锁屏/解锁感知（覆盖自动锁屏与手动 Win+L）
        SystemEvents.SessionSwitch += OnSessionSwitch;

        // 模型加载 + 首轮比对放后台，避免阻塞托盘消息循环
        Task.Run(() =>
        {
            try
            {
                _comparator.Init();
                string verdict = _comparator.RunCycle();
                ShowBalloon(verdict);
            }
            catch (Exception ex)
            {
                Comparator.Log("初始化失败：" + ex.Message);
                ShowBalloon("初始化失败：" + ex.Message);
                return;
            }
            // 之后按设置的间隔循环比对
            TimeSpan interval = Settings.Current.Interval;
            _timer = new System.Threading.Timer(_ => RunNow(), null, interval, interval);
        });

#if TESTMODE
        // 测试专用：模拟锁屏/解锁，验证暂停-恢复逻辑（正式包不含）
        _simLockTimer = new System.Threading.Timer(_ =>
        {
            _paused = true;
            Comparator.Log("TESTMODE 模拟锁屏：暂停检测。");
        }, null, 55_000, Timeout.Infinite);
        _simUnlockTimer = new System.Threading.Timer(_ =>
        {
            _paused = false;
            Comparator.Log("TESTMODE 模拟解锁：恢复检测。");
        }, null, 80_000, Timeout.Infinite);
#endif
    }

#if TESTMODE
    private System.Threading.Timer? _simLockTimer;     // 持有引用防被 GC
    private System.Threading.Timer? _simUnlockTimer;
#endif

    /// <summary>锁屏 → 暂停检测；解锁 → 恢复检测并稍后补一次比对。</summary>
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            _paused = true;
            Comparator.Log("检测暂停：电脑已锁屏（自动或手动），解锁后自动恢复。");
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            _paused = false;
            Comparator.Log("检测恢复：电脑已解锁。");
            // 解锁后稍候补一次比对（确认在用电脑的是本人；非本人会再次锁屏）
            // 注意：定时器须持有引用，否则可能被 GC 提前回收导致回调不执行
            _unlockCheckTimer?.Dispose();
            _unlockCheckTimer = new System.Threading.Timer(_ => RunNow(), null, 10_000, Timeout.Infinite);
        }
    }

    /// <summary>打开设置对话框；保存后按新间隔重排定时器。</summary>
    private void OpenSettings()
    {
        using var form = new SettingsForm(Settings.Current);
        form.SettingsSaved += Reschedule;
        form.ShowDialog();
    }

    /// <summary>
    /// 密码门禁：已设置密码则验证；未设置则引导先创建。
    /// 返回 true 表示放行。
    /// </summary>
    private bool RequirePassword(string action)
    {
        if (!PasswordStore.IsSet)
        {
            using var create = new PasswordDialog(PasswordDialog.Mode.Create, $"首次{action}，请先设置退出密码：");
            if (create.ShowDialog() != DialogResult.OK)
            {
                return false;
            }
            PasswordStore.Set(create.CapturedPassword!);
            ShowBalloon("操作密码已设置，之后退出/设置均需输入该密码。");
            return true;
        }
        using var verify = new PasswordDialog(PasswordDialog.Mode.Verify, $"请输入密码以{action}：");
        return verify.ShowDialog() == DialogResult.OK;
    }

    /// <summary>修改密码：先验证旧密码（如已设置），再设置新密码。</summary>
    private void ChangePassword()
    {
        if (PasswordStore.IsSet)
        {
            using var verify = new PasswordDialog(PasswordDialog.Mode.Verify, "请先验证当前密码：");
            if (verify.ShowDialog() != DialogResult.OK)
            {
                return;
            }
        }
        using var create = new PasswordDialog(PasswordDialog.Mode.Change, "请设置新的退出密码：");
        if (create.ShowDialog() == DialogResult.OK)
        {
            PasswordStore.Set(create.CapturedPassword!);
            ShowBalloon("操作密码已修改。");
        }
    }

    /// <summary>设置变更后：更新定时器周期与托盘提示文字。</summary>
    private void Reschedule()
    {
        TimeSpan interval = Settings.Current.Interval;
        _timer?.Change(interval, interval);
        _icon.Text = $"FaceLock 人脸锁（每 {Settings.Current.IntervalText}）";
    }

    private void RunNow()
    {
        if (_paused)
        {
            return;   // 锁屏期间暂停检测
        }
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return;   // 上一轮还没跑完，跳过
        }
        Task.Run(() =>
        {
            string verdict;
            try
            {
                verdict = _comparator.RunCycle();
            }
            catch (Exception ex)
            {
                verdict = "比对出错：" + ex.Message;
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
            ShowBalloon(verdict);
        });
    }

    private void ShowBalloon(string text)
    {
        try
        {
            _icon.ShowBalloonTip(3000, "FaceLock 比对结果", text, ToolTipIcon.Info);
        }
        catch { }
    }

    private static void OpenLog()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "facelock.log");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "");
        }
        Process.Start(new ProcessStartInfo("notepad.exe", path) { UseShellExecute = true });
    }

    private void ExitApp()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _timer?.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        Application.Exit();
    }

    private static Icon LoadIcon()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            // 优先按固定名取（csproj 中 LogicalName="tray.ico"，与命名空间解耦）
            Stream? s = asm.GetManifestResourceStream("tray.ico");
            if (s is null)
            {
                // 兜底：按后缀扫描（防止命名空间变更导致资源名不匹配）
                string? name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("tray.ico", StringComparison.OrdinalIgnoreCase));
                if (name is not null)
                {
                    s = asm.GetManifestResourceStream(name);
                }
            }
            if (s is not null)
            {
                return new Icon(s);
            }
        }
        catch { }
        return SystemIcons.Application;
    }
}
