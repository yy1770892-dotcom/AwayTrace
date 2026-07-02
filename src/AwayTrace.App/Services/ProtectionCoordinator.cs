using System.IO;
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
        IReadOnlyList<string> recordFolders,
        IReadOnlyList<string> lockedFolders,
        bool lockWorkstation,
        bool protectRegisteredApps,
        ProtectedAppProtectionMode protectedAppMode,
        ProtectedAppScanSpeed protectedAppScanSpeed)
    {
        if (IsProtectionActive)
        {
            return ProtectionStartResult.Failed("이미 보호 중입니다.");
        }

        var allFolders = NormalizeFolders(recordFolders.Concat(lockedFolders)).ToArray();
        var foldersToLock = NormalizeFolders(lockedFolders).ToArray();
        if (allFolders.Length == 0)
        {
            return ProtectionStartResult.Failed("기록 폴더 또는 잠금 폴더를 하나 이상 추가해 주세요.");
        }

        var sessionId = Guid.NewGuid();
        var session = new WatchSession(
            sessionId,
            DateTimeOffset.Now,
            null,
            ProtectionSessionState.Active,
            false,
            BuildFolderSnapshot(recordFolders, lockedFolders),
            null);

        await _database.CreateSessionAsync(session);
        await _monitor.StartAsync(sessionId, allFolders);

        var lockResult = await LockFoldersForSessionAsync(sessionId, foldersToLock);
        if (!lockResult.Success)
        {
            _monitor.Stop();
            await _database.DeleteSessionAsync(sessionId);
            return lockResult;
        }

        if (protectRegisteredApps && protectedAppMode != ProtectedAppProtectionMode.LeaveOpen)
        {
            _appBlocker.Start(sessionId, protectedAppMode, protectedAppScanSpeed);
            await RecordSystemEventAsync(sessionId, protectedAppMode switch
            {
                ProtectedAppProtectionMode.HideWindows => "등록 앱 창 숨김 보호 시작",
                ProtectedAppProtectionMode.Terminate => "등록 앱 종료 보호 시작",
                _ => "등록 앱 보호 시작"
            });
        }

        await RecordSystemEventAsync(sessionId, "보호 시작");

        if (lockWorkstation)
        {
            var workstationLockResult = _lockService.Lock();
            if (!workstationLockResult.Success)
            {
                _appBlocker.Stop();
                await _folderLock.UnlockFoldersAsync(sessionId, foldersToLock);
                _monitor.Stop();
                await _database.DeleteSessionAsync(sessionId);
                return ProtectionStartResult.Failed($"PC 잠금에 실패해 보호 시작을 취소했습니다. 사유: {workstationLockResult.ErrorMessage}");
            }
        }

        _currentSessionId = sessionId;
        return ProtectionStartResult.Ok(sessionId);
    }

    public async Task<ProtectionStartResult> ResumeActiveSessionAsync(
        WatchSession session,
        bool protectRegisteredApps,
        ProtectedAppProtectionMode protectedAppMode,
        ProtectedAppScanSpeed protectedAppScanSpeed)
    {
        if (IsProtectionActive)
        {
            return ProtectionStartResult.Failed("이미 보호 중입니다.");
        }

        var allFolders = ParseFolderSnapshot(session.FolderSnapshotJson);
        var foldersToLock = ParseLockedFolderSnapshot(session.FolderSnapshotJson);
        if (allFolders.Count == 0)
        {
            return ProtectionStartResult.Failed("복구할 보호 폴더 정보가 없습니다.");
        }

        if (foldersToLock.Count > 0)
        {
            await _folderLock.UnlockFoldersAsync(session.Id, foldersToLock);
        }

        await _monitor.StartAsync(session.Id, allFolders);

        var lockResult = await LockFoldersForSessionAsync(session.Id, foldersToLock);
        if (!lockResult.Success)
        {
            _monitor.Stop();
            return lockResult;
        }

        if (protectRegisteredApps && protectedAppMode != ProtectedAppProtectionMode.LeaveOpen)
        {
            _appBlocker.Start(session.Id, protectedAppMode, protectedAppScanSpeed);
        }

        _currentSessionId = session.Id;
        // 재부팅 중에는 기록 공백이 있으므로 이 세션은 신뢰도 낮음으로 표시한다.
        // (리포트에서 "기록 신뢰도 낮음"으로 보이게 되어 과장 없이 전달된다.)
        await _database.MarkSessionLowConfidenceAsync(session.Id);
        await RecordSystemEventAsync(session.Id, "재부팅 후 보호 모드 자동 복구 - 재부팅 중 기록 공백이 있을 수 있습니다.");
        return ProtectionStartResult.Ok(session.Id);
    }

    public async Task<Guid?> StopProtectionAsync()
    {
        if (_currentSessionId is null)
        {
            return null;
        }

        var sessionId = _currentSessionId.Value;
        var session = await _database.GetSessionAsync(sessionId);
        var lockedFolders = ParseLockedFolderSnapshot(session?.FolderSnapshotJson);

        await RecordSystemEventAsync(sessionId, "보호 종료");
        _appBlocker.Stop();
        await _folderLock.UnlockFoldersAsync(sessionId, lockedFolders);
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

    public static IReadOnlyList<string> ParseFolderSnapshot(string? folderSnapshotJson)
    {
        return ParseFolderSnapshotEntries(folderSnapshotJson)
            .Select(entry => entry.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> ParseLockedFolderSnapshot(string? folderSnapshotJson)
    {
        return ParseFolderSnapshotEntries(folderSnapshotJson)
            .Where(entry => entry.Kind == MonitoredFolderKind.Locked)
            .Select(entry => entry.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<ProtectionStartResult> LockFoldersForSessionAsync(Guid sessionId, IReadOnlyList<string> foldersToLock)
    {
        if (foldersToLock.Count == 0)
        {
            return ProtectionStartResult.Ok(sessionId);
        }

        var folderLockResult = await _folderLock.LockFoldersAsync(sessionId, foldersToLock);
        if (!folderLockResult.Success)
        {
            if (folderLockResult.LockedFolders.Count > 0)
            {
                await _folderLock.UnlockFoldersAsync(sessionId, foldersToLock);
            }

            return ProtectionStartResult.Failed(
                "잠금 폴더 보호에 실패했습니다.\n"
                + string.Join(Environment.NewLine, folderLockResult.Errors));
        }

        await RecordSystemEventAsync(sessionId, "잠금 폴더 접근 차단 시작");
        return ProtectionStartResult.Ok(sessionId);
    }

    private static string BuildFolderSnapshot(
        IReadOnlyList<string> recordFolders,
        IReadOnlyList<string> lockedFolders)
    {
        var entries = recordFolders
            .Select(path => new FolderSnapshotEntry(Path.GetFullPath(path), MonitoredFolderKind.RecordOnly))
            .Concat(lockedFolders.Select(path => new FolderSnapshotEntry(Path.GetFullPath(path), MonitoredFolderKind.Locked)))
            .GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Any(entry => entry.Kind == MonitoredFolderKind.Locked)
                ? group.First(entry => entry.Kind == MonitoredFolderKind.Locked)
                : group.First())
            .ToArray();

        return JsonSerializer.Serialize(entries, JsonOptions);
    }

    private static IReadOnlyList<FolderSnapshotEntry> ParseFolderSnapshotEntries(string? folderSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(folderSnapshotJson))
        {
            return [];
        }

        try
        {
            var entries = JsonSerializer.Deserialize<FolderSnapshotEntry[]>(folderSnapshotJson, JsonOptions);
            if (entries is not null)
            {
                return entries;
            }
        }
        catch (JsonException)
        {
            try
            {
                var legacyPaths = JsonSerializer.Deserialize<string[]>(folderSnapshotJson, JsonOptions) ?? [];
                return legacyPaths
                    .Select(path => new FolderSnapshotEntry(path, MonitoredFolderKind.Locked))
                    .ToArray();
            }
            catch (JsonException)
            {
                return [];
            }
        }

        return [];
    }

    private static IEnumerable<string> NormalizeFolders(IEnumerable<string> folders)
    {
        return folders
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
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

    private sealed record FolderSnapshotEntry(string Path, MonitoredFolderKind Kind);
}
