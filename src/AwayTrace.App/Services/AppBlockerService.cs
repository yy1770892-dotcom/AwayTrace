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
    private static readonly TimeSpan AppListCacheDuration = TimeSpan.FromSeconds(5);

    private readonly AwayTraceDatabase _database;
    private readonly Dictionary<string, DateTimeOffset> _lastLogged = new(StringComparer.OrdinalIgnoreCase);
    // 숨긴 창 핸들과 그 창의 소유 프로세스 ID.
    // 핸들은 Windows가 재사용할 수 있으므로 복원 시 PID가 일치하는지 확인한다.
    private readonly Dictionary<IntPtr, int> _hiddenWindows = [];
    private readonly object _sync = new();
    private System.Threading.Timer? _timer;
    private volatile bool _isActive;
    private Guid? _sessionId;
    private ProtectedAppProtectionMode _mode = ProtectedAppProtectionMode.HideWindows;
    private int _scanState;
    private bool _disposed;
    // 보호 앱 목록 캐시. 고속 100ms 모드에서 매 스캔마다 DB를 열지 않도록
    // 5초 간격으로만 다시 읽는다.
    private ProtectedApp[] _cachedApps = [];
    private DateTimeOffset _appsCachedAt;

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
        _cachedApps = [];
        _appsCachedAt = default;
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
            var now = DateTimeOffset.Now;
            if (now - _appsCachedAt >= AppListCacheDuration)
            {
                _cachedApps = (await _database.GetProtectedAppsAsync())
                    .Where(app => app.IsEnabled)
                    .ToArray();
                _appsCachedAt = now;
            }

            foreach (var app in _cachedApps)
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
        // 예전 데이터나 다른 경로로 핵심 프로세스가 등록돼 있어도
        // 여기서 최종 차단한다 (피커 필터의 백스톱).
        if (CriticalProcessGuard.IsCritical(app.ProcessName))
        {
            return Task.CompletedTask;
        }

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
                            _hiddenWindows[windowHandle] = process.Id;
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
        KeyValuePair<IntPtr, int>[] entries;
        lock (_sync)
        {
            entries = _hiddenWindows.ToArray();
            _hiddenWindows.Clear();
        }

        foreach (var (windowHandle, ownerProcessId) in entries)
        {
            if (!IsWindow(windowHandle))
            {
                continue;
            }

            // 핸들이 다른 창으로 재사용됐을 수 있으므로,
            // 숨길 당시의 프로세스가 여전히 소유자인지 확인한 뒤 복원한다.
            GetWindowThreadProcessId(windowHandle, out var currentProcessId);
            if (currentProcessId == ownerProcessId)
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
