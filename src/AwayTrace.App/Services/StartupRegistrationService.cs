using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace AwayTrace.App.Services;

public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AwayTrace";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return !string.IsNullOrWhiteSpace(key?.GetValue(ValueName) as string);
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            Enable();
        }
        else
        {
            Disable();
        }
    }

    public void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, BuildStartupCommand());
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string BuildStartupCommand()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath)
            && Path.GetFileName(processPath).Equals("AwayTrace.exe", StringComparison.OrdinalIgnoreCase))
        {
            return Quote(processPath) + " --autostart";
        }

        var appBase = AppContext.BaseDirectory;
        var exePath = Path.Combine(appBase, "AwayTrace.exe");
        if (File.Exists(exePath))
        {
            return Quote(exePath) + " --autostart";
        }

        var dllPath = Path.Combine(appBase, "AwayTrace.dll");
        if (File.Exists(dllPath))
        {
            var host = Process.GetCurrentProcess().MainModule?.FileName ?? "dotnet";
            return Quote(host) + " " + Quote(dllPath) + " --autostart";
        }

        throw new InvalidOperationException("자동 실행에 등록할 AwayTrace 실행 파일을 찾을 수 없습니다.");
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
