using System.Text.Encodings.Web;
using System.Text.Json;
using AwayTrace.Core.Models;
using AwayTrace.Core.Services;
using AwayTrace.Core.Storage;

namespace AwayTrace.App.Services;

public sealed class ProtectionCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly AwayTraceDatabase _database;
    private readonly FileMonitorService _monitor;
    private readonly AppBlockerService _appBlocker;
    private readonly FolderLockService _folderLock;
    private readonly IWorkstationLockService _lockService;
    private Guid? _currentSessionId;

    public ProtectionCoordinator(
        AwayTraceDatabase database,
        FileMonitorService monitor,
        AppBlockerService appBlocker,
        FolderLockService folderLock,
        IWorkstationLockService lockService)
    {
        _database = database;
        _monitor = monitor;
        _appBlocker = appBlocker;
        _folderLock = folderLock;
        _lockService = lockService;
    }

    public bool IsProtectionActive => _currentSessionId is not null;

    public Guid? CurrentSessionId => _currentSessionId;

    public async Task<ProtectionStartResult> StartProtectionAsync(
        IReadOnlyList<string> folders,
        bool lockWorkstation,
        bool blockProtectedApps,
        bool lockProtectedFolders)
    {
        if (IsProtectionActive)
        {
            return ProtectionStartResult.Failed("이미 보호 중입니다.");
        }

        if (folders.Count == 0)
        {
            return ProtectionStartResult.Failed("감시 폴더를 하나 이상 추가해 주세요.");
        }

        var sessionId = Guid.NewGuid();
        var session = new WatchSession(
            sessionId,
            DateTimeOffset.Now,
            null,
            ProtectionSessionState.Active,
            false,
            JsonSerializer.Serialize(folders, JsonOptions),
            null);

        await _database.CreateSessionAsync(session);
        await _monitor.StartAsync(sessionId, folders);
        if (lockProtectedFolders)
        {
            var folderLockResult = await _folderLock.LockFoldersAsync(sessionId, folders);
            if (!folderLockResult.Success)
            {
                if (folderLockResult.LockedFolders.Count > 0)
                {
                    await _folderLock.UnlockFoldersAsync(sessionId);
                }

                _monitor.Stop();
                await _database.DeleteSessionAsync(sessionId);
                return ProtectionStartResult.Failed(
                    "보호 폴더 잠금에 실패해 보호 시작을 취소했습니다.\n"
                    + string.Join(Environment.NewLine, folderLockResult.Errors));
            }

            await RecordSystemEventAsync(sessionId, "보호 폴더 읽기/복사 차단 시작");
        }

        if (blockProtectedApps)
        {
            _appBlocker.Start(sessionId);
            await RecordSystemEventAsync(sessionId, "보호 앱 차단 시작");
        }

        await RecordSystemEventAsync(sessionId, "보호 시작");

        if (lockWorkstation)
        {
            var lockResult = _lockService.Lock();
            if (!lockResult.Success)
            {
                _appBlocker.Stop();
                if (lockProtectedFolders)
                {
                    await _folderLock.UnlockFoldersAsync(sessionId);
                }

                _monitor.Stop();
                await _database.DeleteSessionAsync(sessionId);
                return ProtectionStartResult.Failed($"PC 잠금에 실패해 보호 시작을 취소했습니다. 사유: {lockResult.ErrorMessage}");
            }
        }

        _currentSessionId = sessionId;
        return ProtectionStartResult.Ok(sessionId);
    }

    public async Task<Guid?> StopProtectionAsync()
    {
        if (_currentSessionId is null)
        {
            return null;
        }

        var sessionId = _currentSessionId.Value;
        await RecordSystemEventAsync(sessionId, "보호 종료");
        _appBlocker.Stop();
        await _folderLock.UnlockFoldersAsync(sessionId);
        _monitor.Stop();
        await _database.EndSessionAsync(sessionId, DateTimeOffset.Now);
        _currentSessionId = null;
        return sessionId;
    }

    public Task RecordWindowsSessionEventAsync(string description)
    {
        return _currentSessionId is null
            ? Task.CompletedTask
            : RecordSystemEventAsync(_currentSessionId.Value, description);
    }

    private Task RecordSystemEventAsync(Guid sessionId, string description)
    {
        return _database.AddFileEventAsync(new FileEventRecord(
            0,
            sessionId,
            DateTimeOffset.Now,
            FileEventType.System,
            description,
            null));
    }
}
