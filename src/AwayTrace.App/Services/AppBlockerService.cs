using System.Diagnostics;
using AwayTrace.Core.Models;
using AwayTrace.Core.Storage;

namespace AwayTrace.App.Services;

public sealed class AppBlockerService : IDisposable
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LogDebounce = TimeSpan.FromSeconds(5);

    private readonly AwayTraceDatabase _database;
    private readonly Dictionary<string, DateTimeOffset> _lastLogged = new(StringComparer.OrdinalIgnoreCase);
    private System.Threading.Timer? _timer;
    private Guid? _sessionId;
    private bool _isScanning;
    private bool _disposed;

    public AppBlockerService(AwayTraceDatabase database)
    {
        _database = database;
    }

    public void Start(Guid sessionId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stop();
        _sessionId = sessionId;
        _timer = new System.Threading.Timer(_ => _ = ScanAndBlockAsync(), null, TimeSpan.Zero, ScanInterval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _sessionId = null;
        _lastLogged.Clear();
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

    private async Task ScanAndBlockAsync()
    {
        if (_isScanning || _sessionId is null)
        {
            return;
        }

        try
        {
            _isScanning = true;
            var apps = (await _database.GetProtectedAppsAsync())
                .Where(app => app.IsEnabled)
                .ToArray();

            foreach (var app in apps)
            {
                await BlockProcessesAsync(app);
            }
        }
        finally
        {
            _isScanning = false;
        }
    }

    private async Task BlockProcessesAsync(ProtectedApp app)
    {
        var currentProcessId = Environment.ProcessId;
        foreach (var process in Process.GetProcessesByName(app.ProcessName))
        {
            try
            {
                var processId = process.Id;
                if (processId == currentProcessId)
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                await LogBlockedAttemptAsync(app, $"차단된 앱 실행 시도: {app.DisplayName} ({app.ProcessName}.exe, PID {processId})");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                await LogBlockedAttemptAsync(app, $"앱 차단 실패: {app.DisplayName} ({app.ProcessName}.exe) - {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private Task LogBlockedAttemptAsync(ProtectedApp app, string message)
    {
        if (_sessionId is not Guid sessionId)
        {
            return Task.CompletedTask;
        }

        var now = DateTimeOffset.Now;
        if (_lastLogged.TryGetValue(app.ProcessName, out var previous) && now - previous < LogDebounce)
        {
            return Task.CompletedTask;
        }

        _lastLogged[app.ProcessName] = now;
        return _database.AddFileEventAsync(new FileEventRecord(
            0,
            sessionId,
            now,
            FileEventType.System,
            message,
            null));
    }
}
