using AwayTrace.Core.Models;

namespace AwayTrace.App.ViewModels;

public sealed class ProtectedAppItem : ObservableObject
{
    private bool _isEnabled;

    public ProtectedAppItem(ProtectedApp app)
    {
        Id = app.Id;
        DisplayName = app.DisplayName;
        ProcessName = app.ProcessName;
        ExecutablePath = app.ExecutablePath;
        _isEnabled = app.IsEnabled;
    }

    public long Id { get; }

    public string DisplayName { get; }

    public string ProcessName { get; }

    public string? ExecutablePath { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                EnabledChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? EnabledChanged;
}
