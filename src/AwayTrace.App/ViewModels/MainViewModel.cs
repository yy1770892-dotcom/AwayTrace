using System.Collections.ObjectModel;
using System.Windows.Input;
using AwayTrace.App.Services;
using AwayTrace.Core.Models;
using AwayTrace.Core.Storage;

namespace AwayTrace.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AwayTraceDatabase _database;
    private readonly IFolderPickerService _folderPicker;
    private readonly IProtectedAppPickerService _protectedAppPicker;
    private readonly ProtectionCoordinator _protection;
    private readonly StartupRegistrationService _startupRegistration;
    private readonly RelayCommand _removeFolderCommand;
    private readonly RelayCommand _toggleProtectionCommand;
    private readonly RelayCommand _removeProtectedAppCommand;
    private MonitoredFolder? _selectedFolder;
    private ProtectedAppItem? _selectedProtectedApp;
    private bool _isProtectionActive;
    private bool _isStartWithWindowsEnabled;
    private bool _blockProtectedAppsEnabled;
    private bool _lockProtectedFoldersEnabled;
    private bool _lockWorkstationOnProtectionStart = true;
    private bool _hotkeyEnabled = true;
    private bool _isLoadingOptions;
    private string _hotkeyText = DefaultHotkeyText;
    private string _customAppDisplayName = string.Empty;
    private string _customAppProcessName = string.Empty;

    public const string DefaultHotkeyText = "Ctrl+Alt+A";

    public MainViewModel(
        AwayTraceDatabase database,
        IFolderPickerService folderPicker,
        IProtectedAppPickerService protectedAppPicker,
        ProtectionCoordinator protection,
        StartupRegistrationService startupRegistration)
    {
        _database = database;
        _folderPicker = folderPicker;
        _protectedAppPicker = protectedAppPicker;
        _protection = protection;
        _startupRegistration = startupRegistration;
        Folders = [];

        AddFolderCommand = new RelayCommand(AddFolderAsync, () => !IsProtectionActive);
        _removeFolderCommand = new RelayCommand(RemoveSelectedFolderAsync, () => SelectedFolder is not null && !IsProtectionActive);
        _toggleProtectionCommand = new RelayCommand(ToggleProtectionAsync);
        _removeProtectedAppCommand = new RelayCommand(RemoveSelectedProtectedAppAsync, () => SelectedProtectedApp is not null && !IsProtectionActive);
        RecentReportCommand = new RelayCommand(() => OpenLatestReportRequested?.Invoke(this, EventArgs.Empty));
        AddKakaoTalkCommand = new RelayCommand(() => AddProtectedAppAsync("카카오톡", "KakaoTalk"));
        AddNateOnCommand = new RelayCommand(AddNateOnPresetAsync);
        AddRunningAppCommand = new RelayCommand(AddRunningAppAsync);
        AddCustomProtectedAppCommand = new RelayCommand(AddCustomProtectedAppAsync, () => !string.IsNullOrWhiteSpace(CustomAppProcessName));
        ChangePinCommand = new RelayCommand(() => PinChangeRequested?.Invoke(this, EventArgs.Empty));
        ResetHotkeyCommand = new RelayCommand(() => HotkeyText = DefaultHotkeyText);
    }

    public ObservableCollection<MonitoredFolder> Folders { get; }

    public ObservableCollection<ProtectedAppItem> ProtectedApps { get; } = [];

    public MonitoredFolder? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (SetProperty(ref _selectedFolder, value))
            {
                _removeFolderCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ProtectedAppItem? SelectedProtectedApp
    {
        get => _selectedProtectedApp;
        set
        {
            if (SetProperty(ref _selectedProtectedApp, value))
            {
                _removeProtectedAppCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsProtectionActive
    {
        get => _isProtectionActive;
        private set
        {
            if (SetProperty(ref _isProtectionActive, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(ProtectionButtonText));
                RaiseCommandStates();
            }
        }
    }

    public string StatusText => IsProtectionActive ? "보호 중" : "보호 해제";

    public string ProtectionButtonText => IsProtectionActive ? "보호 종료" : "보호 시작";

    public string FolderLockStatusText => LockProtectedFoldersEnabled
        ? "읽기/복사 차단 켜짐"
        : "읽기/복사 차단 꺼짐";

    public bool IsStartWithWindowsEnabled
    {
        get => _isStartWithWindowsEnabled;
        set
        {
            if (!SetProperty(ref _isStartWithWindowsEnabled, value))
            {
                return;
            }

            try
            {
                _startupRegistration.SetEnabled(value);
            }
            catch (Exception ex)
            {
                UserMessageRequested?.Invoke(this, $"Windows 자동 실행 설정을 변경하지 못했습니다.\n{ex.Message}");
                SetProperty(ref _isStartWithWindowsEnabled, _startupRegistration.IsEnabled(), nameof(IsStartWithWindowsEnabled));
            }
        }
    }

    public bool BlockProtectedAppsEnabled
    {
        get => _blockProtectedAppsEnabled;
        set
        {
            if (SetProperty(ref _blockProtectedAppsEnabled, value))
            {
                SaveBoolSettingAsync("options.block_protected_apps", value);
            }
        }
    }

    public bool LockProtectedFoldersEnabled
    {
        get => _lockProtectedFoldersEnabled;
        set
        {
            if (SetProperty(ref _lockProtectedFoldersEnabled, value))
            {
                OnPropertyChanged(nameof(FolderLockStatusText));
                SaveBoolSettingAsync("options.lock_protected_folders", value);
            }
        }
    }

    public bool LockWorkstationOnProtectionStart
    {
        get => _lockWorkstationOnProtectionStart;
        set
        {
            if (SetProperty(ref _lockWorkstationOnProtectionStart, value))
            {
                SaveBoolSettingAsync("options.lock_workstation_on_start", value);
            }
        }
    }

    public bool HotkeyEnabled
    {
        get => _hotkeyEnabled;
        set
        {
            if (SetProperty(ref _hotkeyEnabled, value))
            {
                SaveBoolSettingAsync("options.hotkey_enabled", value);
                HotkeyOptionsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string HotkeyText
    {
        get => _hotkeyText;
        set
        {
            if (SetProperty(ref _hotkeyText, value.Trim()))
            {
                SaveTextSettingAsync("options.hotkey_text", _hotkeyText);
                HotkeyOptionsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string CustomAppDisplayName
    {
        get => _customAppDisplayName;
        set => SetProperty(ref _customAppDisplayName, value);
    }

    public string CustomAppProcessName
    {
        get => _customAppProcessName;
        set
        {
            if (SetProperty(ref _customAppProcessName, value))
            {
                (AddCustomProtectedAppCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand AddFolderCommand { get; }

    public ICommand RemoveFolderCommand => _removeFolderCommand;

    public ICommand StartProtectionCommand => _toggleProtectionCommand;

    public ICommand RecentReportCommand { get; }

    public ICommand AddKakaoTalkCommand { get; }

    public ICommand AddNateOnCommand { get; }

    public ICommand AddRunningAppCommand { get; }

    public ICommand AddCustomProtectedAppCommand { get; }

    public ICommand RemoveProtectedAppCommand => _removeProtectedAppCommand;

    public ICommand ChangePinCommand { get; }

    public ICommand ResetHotkeyCommand { get; }

    public event EventHandler? ProtectionStarted;

    public event EventHandler? StopProtectionRequested;

    public event EventHandler? OpenLatestReportRequested;

    public event EventHandler? PinChangeRequested;

    public event EventHandler? HotkeyOptionsChanged;

    public event EventHandler<string>? UserMessageRequested;

    public async Task LoadAsync()
    {
        Folders.Clear();
        foreach (var folder in await _database.GetFoldersAsync())
        {
            Folders.Add(folder);
        }

        await LoadProtectedAppsAsync();
        await LoadOptionsAsync();
        RefreshProtectionState(_protection.IsProtectionActive);
        IsStartWithWindowsEnabled = _startupRegistration.IsEnabled();
    }

    public void RefreshProtectionState(bool isActive)
    {
        IsProtectionActive = isActive;
    }

    private async Task AddFolderAsync()
    {
        var folder = _folderPicker.PickFolder();
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        await _database.AddFolderAsync(folder);
        await LoadAsync();
    }

    private async Task RemoveSelectedFolderAsync()
    {
        if (SelectedFolder is null)
        {
            return;
        }

        await _database.RemoveFolderAsync(SelectedFolder.Id);
        SelectedFolder = null;
        await LoadAsync();
    }

    private async Task ToggleProtectionAsync()
    {
        if (IsProtectionActive)
        {
            StopProtectionRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        await StartProtectionAsync();
    }

    private async Task StartProtectionAsync()
    {
        var folders = Folders.Select(folder => folder.Path).ToArray();
        var result = await _protection.StartProtectionAsync(
            folders,
            LockWorkstationOnProtectionStart,
            BlockProtectedAppsEnabled,
            LockProtectedFoldersEnabled);
        if (!result.Success)
        {
            UserMessageRequested?.Invoke(this, result.ErrorMessage ?? "보호 시작에 실패했습니다.");
            return;
        }

        RefreshProtectionState(true);
        ProtectionStarted?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseCommandStates()
    {
        (AddFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
        _removeFolderCommand.RaiseCanExecuteChanged();
        _toggleProtectionCommand.RaiseCanExecuteChanged();
        _removeProtectedAppCommand.RaiseCanExecuteChanged();
    }

    private async Task LoadProtectedAppsAsync()
    {
        foreach (var app in ProtectedApps)
        {
            app.EnabledChanged -= OnProtectedAppEnabledChanged;
        }

        ProtectedApps.Clear();
        foreach (var app in await _database.GetProtectedAppsAsync())
        {
            var item = new ProtectedAppItem(app);
            item.EnabledChanged += OnProtectedAppEnabledChanged;
            ProtectedApps.Add(item);
        }
    }

    private async Task AddProtectedAppAsync(string displayName, string processName)
    {
        await _database.AddProtectedAppAsync(displayName, processName, null);
        await LoadProtectedAppsAsync();
    }

    private async Task AddRunningAppAsync()
    {
        var candidate = _protectedAppPicker.PickRunningApp();
        if (candidate is null)
        {
            return;
        }

        await _database.AddProtectedAppAsync(candidate.DisplayName, candidate.ProcessName, candidate.ExecutablePath);
        await LoadProtectedAppsAsync();
    }

    private async Task AddNateOnPresetAsync()
    {
        await _database.AddProtectedAppAsync("네이트온", "NateOnMain", null);
        await _database.AddProtectedAppAsync("네이트온", "NateOn", null);
        await LoadProtectedAppsAsync();
    }

    private async Task AddCustomProtectedAppAsync()
    {
        var processName = CustomAppProcessName.Trim();
        var displayName = string.IsNullOrWhiteSpace(CustomAppDisplayName)
            ? processName
            : CustomAppDisplayName.Trim();

        await AddProtectedAppAsync(displayName, processName);
        CustomAppDisplayName = string.Empty;
        CustomAppProcessName = string.Empty;
    }

    private async Task RemoveSelectedProtectedAppAsync()
    {
        if (SelectedProtectedApp is null)
        {
            return;
        }

        await _database.RemoveProtectedAppAsync(SelectedProtectedApp.Id);
        SelectedProtectedApp = null;
        await LoadProtectedAppsAsync();
    }

    private async void OnProtectedAppEnabledChanged(object? sender, EventArgs e)
    {
        if (sender is ProtectedAppItem item)
        {
            await _database.UpdateProtectedAppEnabledAsync(item.Id, item.IsEnabled);
        }
    }

    private async Task LoadOptionsAsync()
    {
        _isLoadingOptions = true;
        try
        {
            BlockProtectedAppsEnabled = await GetBoolSettingAsync("options.block_protected_apps", defaultValue: false);
            LockProtectedFoldersEnabled = await GetBoolSettingAsync("options.lock_protected_folders", defaultValue: false);
            LockWorkstationOnProtectionStart = await GetBoolSettingAsync("options.lock_workstation_on_start", defaultValue: true);
            HotkeyEnabled = await GetBoolSettingAsync("options.hotkey_enabled", defaultValue: true);
            HotkeyText = await _database.GetSettingAsync("options.hotkey_text") ?? DefaultHotkeyText;
        }
        finally
        {
            _isLoadingOptions = false;
        }
    }

    private async Task<bool> GetBoolSettingAsync(string key, bool defaultValue)
    {
        var value = await _database.GetSettingAsync(key);
        return value is null ? defaultValue : value == "1";
    }

    private void SaveBoolSettingAsync(string key, bool value)
    {
        if (_isLoadingOptions)
        {
            return;
        }

        _ = _database.SetSettingAsync(key, value ? "1" : "0");
    }

    private void SaveTextSettingAsync(string key, string value)
    {
        if (_isLoadingOptions)
        {
            return;
        }

        _ = _database.SetSettingAsync(key, value);
    }
}
