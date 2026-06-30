namespace AwayTrace.Core.Models;

public enum PinVerifyStatus
{
    Success,
    Failed,
    Locked,
    NotConfigured
}

public sealed record PinVerifyResult(
    PinVerifyStatus Status,
    int RemainingAttempts,
    TimeSpan? RetryAfter)
{
    public static PinVerifyResult Success() => new(PinVerifyStatus.Success, 5, null);

    public static PinVerifyResult Failed(int remainingAttempts) =>
        new(PinVerifyStatus.Failed, remainingAttempts, null);

    public static PinVerifyResult Locked(TimeSpan retryAfter) =>
        new(PinVerifyStatus.Locked, 0, retryAfter);

    public static PinVerifyResult NotConfigured() =>
        new(PinVerifyStatus.NotConfigured, 0, null);
}
