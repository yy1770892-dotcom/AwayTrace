using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using AwayTrace.Core.Models;
using AwayTrace.Core.Storage;

namespace AwayTrace.App.Services;

public sealed class FolderLockService
{
    private readonly AwayTraceDatabase _database;
    private readonly string _identity;
    private readonly List<string> _lockedFolders = [];

    public FolderLockService(AwayTraceDatabase database)
    {
        _database = database;
        _identity = WindowsIdentity.GetCurrent().Name;
    }

    public IReadOnlyList<string> LockedFolders => _lockedFolders;

    public async Task<FolderLockResult> LockFoldersAsync(Guid sessionId, IReadOnlyList<string> folders)
    {
        _lockedFolders.Clear();
        var errors = new List<string>();

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                errors.Add($"{folder}: 폴더가 존재하지 않습니다.");
                continue;
            }

            var result = await RunIcaclsAsync(folder, "/deny", $"{_identity}:(OI)(CI)(RX,W,D)");
            if (result.ExitCode == 0)
            {
                _lockedFolders.Add(folder);
                await RecordSystemEventAsync(sessionId, $"잠금 폴더 접근 차단: {folder}");
            }
            else
            {
                errors.Add($"{folder}: {result.ErrorText}");
            }
        }

        if (errors.Count > 0)
        {
            return FolderLockResult.Failed(_lockedFolders.ToArray(), errors);
        }

        return FolderLockResult.Ok(_lockedFolders.ToArray());
    }

    public async Task UnlockFoldersAsync(Guid sessionId, IReadOnlyList<string>? folders = null)
    {
        // 주의: 잠금 상태에서는 현재 사용자에게 RX가 deny라 Directory.Exists가
        // ACCESS_DENIED로 false를 반환할 수 있다. 존재 확인으로 걸러내면
        // 잠긴 폴더가 영영 해제되지 않으므로, 항상 해제를 시도한다.
        var foldersToUnlock = _lockedFolders
            .Concat(folders ?? [])
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var folder in foldersToUnlock)
        {
            var result = await RunIcaclsAsync(folder, "/remove:d", _identity);
            string message;
            if (result.ExitCode == 0)
            {
                message = $"잠금 폴더 접근 차단 해제: {folder}";
            }
            else if (!Directory.Exists(folder))
            {
                message = $"잠금 폴더 접근 차단 해제 건너뜀(폴더 없음): {folder}";
            }
            else
            {
                message = $"잠금 폴더 접근 차단 해제 실패: {folder} - {result.ErrorText}";
            }

            await RecordSystemEventAsync(sessionId, message);
        }

        _lockedFolders.Clear();
    }

    private Task RecordSystemEventAsync(Guid sessionId, string message)
    {
        return _database.AddFileEventAsync(new FileEventRecord(
            0,
            sessionId,
            DateTimeOffset.Now,
            FileEventType.System,
            message,
            null));
    }

    private static async Task<(int ExitCode, string ErrorText)> RunIcaclsAsync(
        string folder,
        string operation,
        string identityArgument)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "icacls.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(folder);
        startInfo.ArgumentList.Add(operation);
        startInfo.ArgumentList.Add(identityArgument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("icacls.exe를 시작할 수 없습니다.");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        return (process.ExitCode, string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim());
    }
}
