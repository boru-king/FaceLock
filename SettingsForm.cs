// SettingsForm.cs —— 设置对话框（托盘菜单打开）：比对间隔（秒/分钟）+ 是否显示命令行窗口
// 布局说明：全部控件留足间距、提示文本分两行短句，避免高 DPI 下遮挡/截断。
using System.Drawing;

internal sealed class SettingsForm : Form
{
    private readonly NumericUpDown _num;
    private readonly ComboBox _unit;
    private readonly CheckBox _showConsole;

    /// <summary>设置保存后触发（托盘用于重排定时器）。</summary>
    internal event Action? SettingsSaved;

    internal SettingsForm(Settings s)
    {
        Text = "设置 - 人脸比对锁屏";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(430, 235);

        // 第 1 行：比对间隔 [数值框] [单位下拉]
        var lblInterval = new Label
        {
            Text = "比对间隔：",
            Location = new Point(26, 30),
            AutoSize = true,
        };
        _num = new NumericUpDown
        {
            Location = new Point(150, 26),
            Size = new Size(120, 28),
            Minimum = 1,
            Maximum = 10080,
            Value = Math.Clamp(s.IntervalValue, 1, 10080),
            TextAlign = HorizontalAlignment.Left,
        };
        _unit = new ComboBox
        {
            Location = new Point(290, 26),
            Size = new Size(110, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _unit.Items.AddRange(new object[] { "分钟", "秒" });
        _unit.SelectedIndexChanged += (_, _) =>
        {
            // 秒：5 ~ 3600；分钟：1 ~ 10080（7 天）
            if (_unit.SelectedIndex == 1)
            {
                _num.Minimum = 5;
                _num.Maximum = 3600;
            }
            else
            {
                _num.Minimum = 1;
                _num.Maximum = 10080;
            }
            _num.Value = Math.Clamp(_num.Value, _num.Minimum, _num.Maximum);
        };
        _unit.SelectedIndex = s.IsSeconds ? 1 : 0;

        // 第 2 行：命令行窗口开关
        _showConsole = new CheckBox
        {
            Text = "比对时显示命令行窗口",
            Location = new Point(26, 76),
            AutoSize = true,
            Checked = s.ShowConsoleWindow,
        };

        // 第 3~4 行：提示（两行短句，避开截断）
        var hint1 = new Label
        {
            Text = "设置保存在 config.json，保存后立即生效。",
            Location = new Point(26, 118),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
        };
        var hint2 = new Label
        {
            Text = "间隔小于单轮耗时（约 15 秒）时，自动跳过重叠轮次。",
            Location = new Point(26, 142),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
        };

        // 按钮行
        var btnSave = new Button { Text = "保存", Location = new Point(195, 182), Size = new Size(95, 32) };
        var btnCancel = new Button { Text = "取消", Location = new Point(305, 182), Size = new Size(95, 32) };
        btnSave.Click += (_, _) =>
        {
            s.IntervalValue = (int)_num.Value;
            s.IntervalUnit = _unit.SelectedIndex == 1 ? "seconds" : "minutes";
            s.ShowConsoleWindow = _showConsole.Checked;
            s.Save();
            SettingsSaved?.Invoke();
            Close();
        };
        btnCancel.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { lblInterval, _num, _unit, _showConsole, hint1, hint2, btnSave, btnCancel });
        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }
}
