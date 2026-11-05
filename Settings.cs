// Settings.cs —— 运行设置（持久化到 exe 旁 config.json）
using System.Text.Json;

internal sealed class Settings
{
    /// <summary>当前生效的设置（启动时从 config.json 加载，无文件则用默认值）。</summary>
    internal static Settings Current { get; private set; } = Load();

    /// <summary>比对间隔数值。</summary>
    public int IntervalValue { get; set; } = 3;

    /// <summary>比对间隔单位："minutes"（分钟）或 "seconds"（秒）。</summary>
    public string IntervalUnit { get; set; } = "minutes";

    /// <summary>比对时是否弹出命令行窗口查看明细。</summary>
    public bool ShowConsoleWindow { get; set; } = true;

    /// <summary>退出/设置密码哈希（PBKDF2-SHA256，Base64；空 = 未设置）。</summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>密码盐（Base64）。</summary>
    public string PasswordSalt { get; set; } = "";

    internal bool IsSeconds => string.Equals(IntervalUnit, "seconds", StringComparison.OrdinalIgnoreCase);

    internal TimeSpan Interval => IsSeconds ? TimeSpan.FromSeconds(IntervalValue) : TimeSpan.FromMinutes(IntervalValue);

    internal string IntervalText => IsSeconds ? $"{IntervalValue} 秒" : $"{IntervalValue} 分钟";

    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "config.json");

    private static Settings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(ConfigPath)) ?? new Settings();
            }
        }
        catch (Exception ex)
        {
            Comparator.Log("设置加载失败，使用默认值：" + ex.Message);
        }
        return new Settings();
    }

    /// <summary>保存到 config.json 并使其立即生效。</summary>
    internal void Save()
    {
        Current = this;
        try
        {
            File.WriteAllText(ConfigPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            Comparator.Log($"设置已保存：间隔 {IntervalText}，比对窗口 {(ShowConsoleWindow ? "显示" : "隐藏")}。");
        }
        catch (Exception ex)
        {
            Comparator.Log("设置保存失败：" + ex.Message);
        }
    }
}
