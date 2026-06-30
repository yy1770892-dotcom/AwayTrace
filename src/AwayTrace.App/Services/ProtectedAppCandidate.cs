namespace AwayTrace.App.Services;

public sealed record ProtectedAppCandidate(
    string DisplayName,
    string ProcessName,
    string? ExecutablePath);
