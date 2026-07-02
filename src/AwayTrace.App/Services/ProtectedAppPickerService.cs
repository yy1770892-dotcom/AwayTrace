using System.Diagnostics;
using AwayTrace.App.Views;

namespace AwayTrace.App.Services;

public sealed class ProtectedAppPickerService : IProtectedAppPickerService
{
    private static readonly HashSet<string> KnownBackgroundApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "KakaoTalk",
        "NateOn",
        "NateOnMain",
        "Teams",
        "Slack",
        "Discord",
        "Telegram",
        "LINE"
    };

    public ProtectedAppCandidate? PickRunningApp()
    {
        var candidates = Process.GetProcesses()
            .Select(TryCreateCandidate)
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .DistinctBy(candidate => candidate.ProcessName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(candidate => candidate.DisplayName)
            .ToArray();

        var window = new RunningAppPickerWindow(candidates);
        return window.ShowDialog() == true ? window.SelectedCandidate : null;
    }

    private static ProtectedAppCandidate? TryCreateCandidate(Process process)
    {
        try
        {
            var processName = process.ProcessName;
            if (CriticalProcessGuard.IsCritical(processName))
            {
                // Windows 핵심 프로세스는 목록에 노출하지 않는다.
                // 종료 모드로 등록되면 바탕화면이 사라지는 등 시스템이 망가질 수 있다.
                return null;
            }

            var title = process.MainWindowTitle;
            if (string.IsNullOrWhiteSpace(title) && !KnownBackgroundApps.Contains(processName))
            {
                return null;
            }

            var displayName = string.IsNullOrWhiteSpace(title) ? processName : title;
            string? path = null;
            try
            {
                path = process.MainModule?.FileName;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                path = null;
            }

            return new ProtectedAppCandidate(displayName, processName, path);
        }
        finally
        {
            process.Dispose();
        }
    }
}
