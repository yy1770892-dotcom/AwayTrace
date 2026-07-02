using AwayTrace.Core.Models;

namespace AwayTrace.App.ViewModels;

public sealed record ProtectedAppScanSpeedOption(
    ProtectedAppScanSpeed Speed,
    string Name,
    string Description);
