// PasswordStore.cs —— 退出/设置密码存取（PBKDF2-SHA256 哈希 + 随机盐，不存明文）
using System.Security.Cryptography;
using System.Text;

internal static class PasswordStore
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    /// <summary>是否已设置密码。</summary>
    internal static bool IsSet => !string.IsNullOrEmpty(Settings.Current.PasswordHash);

    /// <summary>设置（或修改）密码，保存到 config.json。</summary>
    internal static void Set(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        Settings.Current.PasswordSalt = Convert.ToBase64String(salt);
        Settings.Current.PasswordHash = Convert.ToBase64String(Hash(password, salt));
        Settings.Current.Save();
    }

    /// <summary>验证密码（定长时间比较，防时序侧信道）。</summary>
    internal static bool Verify(string password)
    {
        if (!IsSet)
        {
            return false;
        }
        try
        {
            byte[] salt = Convert.FromBase64String(Settings.Current.PasswordSalt);
            byte[] expected = Convert.FromBase64String(Settings.Current.PasswordHash);
            return CryptographicOperations.FixedTimeEquals(expected, Hash(password, salt));
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Hash(string password, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashSize);
}
