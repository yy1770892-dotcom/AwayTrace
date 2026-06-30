namespace AwayTrace.App.Services;

public sealed record ProtectionStartResult(bool Success, Guid? SessionId, string? ErrorMessage)
{
    public static ProtectionStartResult Ok(Guid sessionId) => new(true, sessionId, null);

    public static ProtectionStartResult Failed(string message) => new(false, null, message);
}
