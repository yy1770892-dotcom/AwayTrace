namespace AwayTrace.App.Services;

public sealed record WorkstationLockResult(bool Success, string? ErrorMessage)
{
    public static WorkstationLockResult Ok() => new(true, null);

    public static WorkstationLockResult Failed(string message) => new(false, message);
}
