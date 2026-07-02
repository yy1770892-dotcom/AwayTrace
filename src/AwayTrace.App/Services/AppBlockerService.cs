using System.Diagnostics;
using System.Runtime.InteropServices;
using AwayTrace.Core.Models;
using AwayTrace.Core.Storage;

namespace AwayTrace.App.Services;

public sealed class AppBlockerService : IDisposable
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private static readonly TimeSpan LogDebounce = TimeSpan.FromSeconds(5);

    private readonly AwayTraceDatabase _database;
    private readonly Dictionary<string, DateTimeOffset> _lastLogged = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<IntPtr> _hiddenWindows = [];
    private readonly object _sync = new();
    private System.Threading.Timer? _timer;
    private volatile bool _isActive;
    private Guid? _sessionId;
    private ProtectedAppProtectionMode _mode = ProtectedAppProtectionMode.HideWindows;
    private int _scanState;
    private bool _disposed;

    public AppBlockerService(AwayTraceDatabase database)
    {
        _database = database;
    }

    public void Start(Guid sessionId, ProtectedAppProtectionMode mode, ProtectedAppScanSpeed scanSpeed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stop();
        _sessionId = sessionId;
        _mode = mode;
        _isActive = true;
        if (_mode != ProtectedAppProtectionMode.LeaveOpen)
        {
            var scanInterval = TimeSpan.FromMilliseconds((int)scanSpeed);
            _timer = new System.Threading.Timer(_ => _ = ScanAndApplyPolicyAsync(), null, TimeSpan.Zero, scanInterval);
        }
    }

    public void Stop()
    {
        // 진행 중인 스캔이 늦게 창을 숨기지 않도록 먼저 비활성화한다.
        _isActive = false;
        _timer?.Dispose();
        _timer = null;
        RestoreHiddenWindows();
        _sessionId = null;
        lock (_sync)
        {
            _lastLogged.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    private async Task ScanAndApplyPolicyAsync()
    {
        if (!_isActive || _sessionId is null)
        {
            return;
        }

        // System.Threading.Timer는 이전 콜백이 끝나지 않아도 다시 발화할 수 있으므로
        // Interlocked로 재진입을 차단한다.
        if (Interlocked.CompareExchange(ref _scanState, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var apps = (await _database.GetProtectedAppsAsync())
                .Where(app => app.IsEnabled)
                .ToArray();

            foreach (var app in apps)
            {
                if (!_isActive)
                {
                    return;
                }

                await ApplyPolicyAsync(app);
            }
        }
        catch
        {
            // fire-and-forget 타이머 콜백에서 예외가 새어 나가면
            // UnobservedTaskException으로 이어지므로 여기서 흡수한다.
        }
        finally
        {
            Volatile.Write(ref _scanState, 0);
        }
    }

    private Task ApplyPolicyAsync(ProtectedApp app)
    {
        return _mode switch
        {
            ProtectedAppProtectionMode.HideWindows => HideAppWindowsAsync(app),
            ProtectedAppProtectionMode.Terminate => TerminateAppProcessesAsync(app),
            _ => Task.CompletedTask
        };
    }

    private async Task HideAppWindowsAsync(ProtectedApp app)
    {
        foreach (var process in Process.GetProcessesByName(app.ProcessName))
        {
            try
            {
                if (!_isActive)
                {
                    return;
                }

                var windows = GetVisibleTopLevelWindows(process.Id);
                foreach (var windowHandle in windows)
                {
                    if (!_isActive)
                    {
                        return;
                    }

                    if (ShowWindow(windowHandle, SwHide))
                    {
                        lock (_sync)
                        {
                            _hiddenWindows.Add(windowHandle);
                        }

                        await LogProtectedAppEventAsync(
                            app,
                            $"등록 앱 창 숨김: {app.DisplayName} ({app.ProcessName}.exe, PID {process.Id})");
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                await LogProtectedAppEventAsync(app, $"등록 앱 창 숨김 실패: {app.DisplayName} ({app.ProcessName}.exe) - {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private async Task TerminateAppProcessesAsync(ProtectedApp app)
    {
        var currentProcessId = Environment.ProcessId;
        foreach (var process in Process.GetProcessesByName(app.ProcessName))
        {
            try
            {
                if (!_isActive)
                {
                    return;
                }

                var processId = process.Id;
                if (processId == currentProcessId)
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                await LogProtectedAppEventAsync(app, $"등록 앱 종료: {app.DisplayName} ({app.ProcessName}.exe, PID {processId})");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                await LogProtectedAppEventAsync(app, $"등록 앱 종료 실패: {app.DisplayName} ({app.ProcessName}.exe) - {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private void RestoreHiddenWindows()
    {
        IntPtr[] handles;
        lock (_sync)
        {
            handles = _hiddenWindows.ToArray();
            _hiddenWindows.Clear();
        }

        foreach (var windowHandle in handles)
        {
            if (IsWindow(windowHandle))
            {
                ShowWindow(windowHandle, SwShow);
            }
        }
    }

    private static IReadOnlyList<IntPtr> GetVisibleTopLevelWindows(int processId)
    {
        var windows = new List<IntPtr>();
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var ownerProcessId);
            if (ownerProcessId == processId && IsWindowVisible(handle))
            {
                windows.Add(handle);
            }

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    private Task LogProtectedAppEventAsync(ProtectedApp app, string message)
    {
        if (_sessionId is not Guid sessionId)
        {
            return Task.CompletedTask;
        }

        var now = DateTimeOffset.Now;
        var logKey = $"{app.ProcessName}:{message.Split(':')[0]}";
        lock (_sync)
        {
            if (_lastLogged.TryGetValue(logKey, out var previous) && now - previous < LogDebounce)
            {
                return Task.CompletedTask;
            }

            _lastLogged[logKey] = now;
        }

        return _database.AddFileEventAsync(new FileEventRecord(
            0,
            sessionId,
            now,
            FileEventType.System,
            message,
            null));
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
