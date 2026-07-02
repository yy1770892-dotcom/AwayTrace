namespace AwayTrace.App.Services;

/// <summary>
/// 보호 앱으로 등록되면 안 되는 Windows 핵심 프로세스 목록.
/// 종료 모드에서 이런 프로세스를 죽이면 바탕화면이 사라지거나
/// 시스템이 불안정해질 수 있으므로 피커와 블로커 양쪽에서 걸러낸다.
/// </summary>
public static class CriticalProcessGuard
{
    private static readonly HashSet<string> CriticalProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer",
        "dwm",
        "csrss",
        "smss",
        "wininit",
        "winlogon",
        "services",
        "lsass",
        "svchost",
        "fontdrvhost",
        "sihost",
        "taskhostw",
        "ctfmon",
        "RuntimeBroker",
        "SearchHost",
        "StartMenuExperienceHost",
        "ShellExperienceHost",
        "ApplicationFrameHost",
        "SystemSettings",
        "SecurityHealthSystray",
        "MsMpEng",
        "AwayTrace"
    };

    public static bool IsCritical(string processName)
    {
        var trimmed = processName.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        return CriticalProcessNames.Contains(trimmed);
    }
}
