using AwayTrace.App.Services;

namespace AwayTrace.App.ViewModels;

public sealed record ProtectedAppProtectionModeOption(
    ProtectedAppProtectionMode Mode,
    string Name,
    string Description);
