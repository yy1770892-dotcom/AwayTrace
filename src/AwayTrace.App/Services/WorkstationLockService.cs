using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AwayTrace.App.Services;

public sealed class WorkstationLockService : IWorkstationLockService
{
    public WorkstationLockResult Lock()
    {
        if (LockWorkStation())
        {
            return WorkstationLockResult.Ok();
        }

        var error = Marshal.GetLastWin32Error();
        var message = error == 0
            ? "Windows가 워크스테이션 잠금을 거부했습니다."
            : new Win32Exception(error).Message;
        return WorkstationLockResult.Failed(message);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();
}
