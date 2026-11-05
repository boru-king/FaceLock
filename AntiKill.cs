// AntiKill.cs —— 进程防结束保护：拒绝 PROCESS_TERMINATE 与安全描述符改写
// 效果：同一用户的任务管理器"结束任务"会被拒绝；管理员（SeDebugPrivilege）仍可结束——作为找回路径。
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;

internal static class AntiKill
{
    private const int ProcessTerminate = 0x0001;
    private const int WriteDacl = 0x00040000;
    private const int WriteOwner = 0x00080000;
    private const int DaclSecurityInformation = 0x00000004;

    internal static void Protect()
    {
        try
        {
            var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var dacl = new RawAcl(2, 2);

            // 拒绝：结束进程 / 改写安全描述符（防外部解除保护）/ 改写所有者
            int denyMask = ProcessTerminate | WriteDacl | WriteOwner;
            dacl.InsertAce(0,
                new CommonAce(AceFlags.None, AceQualifier.AccessDenied, denyMask, everyone, false, null));
            // 允许：其余全部权限（保证程序自身与系统正常访问）
            dacl.InsertAce(1,
                new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, 0x001FFFFF, everyone, false, null));

            var sd = new RawSecurityDescriptor(ControlFlags.DiscretionaryAclPresent, null, null, null, dacl);
            byte[] buf = new byte[sd.BinaryLength];
            sd.GetBinaryForm(buf, 0);

            Native.SetKernelObjectSecurity(Process.GetCurrentProcess().Handle, DaclSecurityInformation, buf);
            Comparator.Log("防结束保护已启用（任务管理器无法结束本进程）。");
        }
        catch (Exception ex)
        {
            Comparator.Log("防结束保护启用失败：" + ex.Message);
        }
    }
}
