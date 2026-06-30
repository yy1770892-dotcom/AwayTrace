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
                await RecordSystemEventAsync(sessionId, $"보호 폴더 잠금: {folder}");
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

    public async Task UnlockFoldersAsync(Guid sessionId)
    {
        foreach (var folder in _lockedFolders.ToArray())
        {
            var result = await RunIcaclsAsync(folder, "/remove:d", _identity);
            var message = result.ExitCode == 0
                ? $"보호 폴더 잠금 해제: {folder}"
                : $"보호 폴더 잠금 해제 실패: {folder} - {result.ErrorText}";
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
