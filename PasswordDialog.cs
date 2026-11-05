// PasswordDialog.cs —— 密码对话框：设置新密码 / 验证密码 / 修改密码
using System.Drawing;

internal sealed class PasswordDialog : Form
{
    internal enum Mode
    {
        Create,   // 设置新密码（新密码 + 确认）
        Verify,   // 验证现有密码
        Change,   // 修改密码（当前 + 新 + 确认）
    }

    private readonly TextBox _oldBox;
    private readonly TextBox _newBox;
    private readonly TextBox _confirmBox;

    /// <summary>设置/修改模式下用户输入的新密码（确定后有效）。</summary>
    internal string? CapturedPassword { get; private set; }

    internal PasswordDialog(Mode mode, string prompt)
    {
        Text = mode switch
        {
            Mode.Create => "设置密码",
            Mode.Verify => "身份验证",
            _ => "修改密码",
        };
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;

        int rows = mode == Mode.Change ? 3 : mode == Mode.Create ? 2 : 1;
        int btnY = 54 + rows * 46 + 10;
        ClientSize = new Size(420, btnY + 32 + 22);

        var lblPrompt = new Label { Text = prompt, Location = new Point(24, 18), AutoSize = true };
        Controls.Add(lblPrompt);

        int y = 54;
        _oldBox = MakeField("当前密码：", y, visible: mode == Mode.Change);
        if (mode == Mode.Change) y += 46;
        _newBox = MakeField(mode == Mode.Verify ? "密　　码：" : "新　密码：", y, visible: true);   // 验证模式此框即密码输入框，必须可见
        if (mode != Mode.Verify) y += 46;
        _confirmBox = MakeField("确认密码：", y, visible: mode != Mode.Verify);

        // 验证模式：唯一输入框即密码框
        TextBox active = mode switch
        {
            Mode.Verify => _newBox,
            Mode.Create => _newBox,
            _ => _oldBox,
        };

        var btnOk = new Button { Text = "确定", Location = new Point(195, btnY), Size = new Size(95, 32) };
        var btnCancel = new Button { Text = "取消", Location = new Point(305, btnY), Size = new Size(95, 32) };
        btnOk.Click += (_, _) =>
        {
            switch (mode)
            {
                case Mode.Verify:
                    if (PasswordStore.Verify(_newBox.Text))
                    {
                        DialogResult = DialogResult.OK;
                        Close();
                    }
                    else
                    {
                        MessageBox.Show(this, "密码错误，请重试。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        _newBox.SelectAll();
                        _newBox.Focus();
                    }
                    break;

                case Mode.Create:
                    if (_newBox.Text.Length < 4)
                    {
                        MessageBox.Show(this, "密码至少 4 位。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    if (_newBox.Text != _confirmBox.Text)
                    {
                        MessageBox.Show(this, "两次输入的密码不一致。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    CapturedPassword = _newBox.Text;
                    DialogResult = DialogResult.OK;
                    Close();
                    break;

                case Mode.Change:
                    if (!PasswordStore.Verify(_oldBox.Text))
                    {
                        MessageBox.Show(this, "当前密码错误。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        _oldBox.SelectAll();
                        _oldBox.Focus();
                        return;
                    }
                    if (_newBox.Text.Length < 4)
                    {
                        MessageBox.Show(this, "新密码至少 4 位。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    if (_newBox.Text != _confirmBox.Text)
                    {
                        MessageBox.Show(this, "两次输入的新密码不一致。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    CapturedPassword = _newBox.Text;
                    DialogResult = DialogResult.OK;
                    Close();
                    break;
            }
        };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[] { btnOk, btnCancel });
        AcceptButton = btnOk;
        CancelButton = btnCancel;
        active.Focus();
    }

    private TextBox MakeField(string label, int y, bool visible)
    {
        var lbl = new Label { Text = label, Location = new Point(24, y + 4), AutoSize = true, Visible = visible };
        var box = new TextBox
        {
            Location = new Point(110, y),
            Size = new Size(240, 28),
            UseSystemPasswordChar = true,
            Visible = visible,
        };
        Controls.Add(lbl);
        Controls.Add(box);
        return box;
    }
}
