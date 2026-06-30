namespace AwayTrace.App.Services;

public sealed record FolderLockResult(
    bool Success,
    IReadOnlyList<string> LockedFolders,
    IReadOnlyList<string> Errors)
{
    public static FolderLockResult Ok(IReadOnlyList<string> lockedFolders) => new(true, lockedFolders, []);

    public static FolderLockResult Failed(IReadOnlyList<string> lockedFolders, IReadOnlyList<string> errors) =>
        new(false, lockedFolders, errors);
}
