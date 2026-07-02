using System.Collections.ObjectModel;
using System.Windows.Input;
using AwayTrace.App.Services;
using AwayTrace.Core.Models;
using AwayTrace.Core.Services;
using AwayTrace.Core.Storage;

namespace AwayTrace.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private static readonly ProtectedAppProtectionModeOption[] DefaultProtectedAppProtectionModes =
    [
        new(
            ProtectedAppProtectionMode.LeaveOpen,
            "그대로 두기",
            "등록한 앱을 건드리지 않습니다. 보호 중 실행 여부만 기존 방식대로 기록합니다."),
        new(
            ProtectedAppProtectionMode.HideWindows,
            "창 숨기기",
            "보호 중 등록 앱 창을 숨기고, 보호 종료 시 가능한 창은 다시 표시합니다."),
        new(
            ProtectedAppProtectionMode.Terminate,
            "종료하기",
            "보호 시작 후 등록 앱이 실행되어 있으면 종료하고 시도 정황을 기록합니다.")
    ];

    private static readonly ProtectedAppScanSpeedOption[] DefaultProtectedAppScanSpeeds =
    [
        new(
            ProtectedAppScanSpeed.Normal,
            "일반 250ms",
            "권장 기본값입니다. 메신저 창을 주기적으로 확인해 숨깁니다."),
        new(
            ProtectedAppScanSpeed.Fast,
            "고속 100ms",
            "더 빠르게 숨기지만 CPU 사용량이 증가할 수 있습니다.")
    ];

    private readonly AwayTraceDatabase _database;
    private readonly IFolderPickerService _folderPicker;
    private readonly IProtectedAppPickerService _protectedAppPicker;
    private readonly ProtectionCoordinator _protection;
    private readonly StartupRegistrationService _startupRegistration;
    private readonly RelayCommand _removeRecordFolderCommand;
    private readonly RelayCommand _removeLockedFolderCommand;
    private readonly RelayCommand _toggleProtectionCommand;
    private readonly RelayCommand _removeProtectedAppCommand;
    private MonitoredFolder? _selectedRecordFolder;
    private MonitoredFolder? _selectedLockedFolder;
    private ProtectedAppItem? _selectedProtectedApp;
    private ProtectedAppProtectionModeOption _selectedProtectedAppProtectionMode = DefaultProtectedAppProtectionModes[1];
    private ProtectedAppScanSpeedOption _selectedProtectedAppScanSpeed = DefaultProtectedAppScanSpeeds[0];
    private bool _isProtectionActive;
    private bool _isStartWithWindowsEnabled;
    private bool _restoreProtectionAfterRestartEnabled;
    private bool _blockProtectedAppsEnabled;
    private bool _lockWorkstationOnProtectionStart = true;
    private bool _hotkeyEnabled = true;
    private bool _hasPcUsageWarning;
    private bool _isLoadingOptions;
    private string _hotkeyText = DefaultHotkeyText;
    private string _protectedAppStatusMessage = "실행 중인 앱을 선택하면 이 목록에 저장됩니다.";

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

        AddRecordFolderCommand = new RelayCommand(() => AddFolderAsync(MonitoredFolderKind.RecordOnly), () => !IsProtectionActive);
        AddLockedFolderCommand = new RelayCommand(() => AddFolderAsync(MonitoredFolderKind.Locked), () => !IsProtectionActive);
        _removeRecordFolderCommand = new RelayCommand(RemoveSelectedRecordFolderAsync, () => SelectedRecordFolder is not null && !IsProtectionActive);
        _removeLockedFolderCommand = new RelayCommand(RemoveSelectedLockedFolderAsync, () => SelectedLockedFolder is not null && !IsProtectionActive);
        _toggleProtectionCommand = new RelayCommand(ToggleProtectionAsync);
        _removeProtectedAppCommand = new RelayCommand(RemoveSelectedProtectedAppAsync, () => SelectedProtectedApp is not null && !IsProtectionActive);
        RecentReportCommand = new RelayCommand(() => OpenLatestReportRequested?.Invoke(this, EventArgs.Empty));
        PcUsageLogCommand = new RelayCommand(() => OpenPcUsageLogRequested?.Invoke(this, EventArgs.Empty));
        HideWindowCommand = new RelayCommand(() => HideWindowRequested?.Invoke(this, EventArgs.Empty));
        AddKakaoTalkCommand = new RelayCommand(() => AddProtectedAppAsync("카카오톡", "KakaoTalk", null));
        AddNateOnCommand = new RelayCommand(AddNateOnPresetAsync);
        AddRunningAppCommand = new RelayCommand(AddRunningAppAsync);
        ChangePinCommand = new RelayCommand(() => PinChangeRequested?.Invoke(this, EventArgs.Empty));
        ResetHotkeyCommand = new RelayCommand(() => HotkeyText = DefaultHotkeyText);
    }

    public ObservableCollection<MonitoredFolder> RecordFolders { get; } = [];

    public ObservableCollection<MonitoredFolder> LockedFolders { get; } = [];

    public ObservableCollection<ProtectedAppItem> ProtectedApps { get; } = [];

    public IReadOnlyList<ProtectedAppProtectionModeOption> ProtectedAppProtectionModes { get; } = DefaultProtectedAppProtectionModes;

    public IReadOnlyList<ProtectedAppScanSpeedOption> ProtectedAppScanSpeeds { get; } = DefaultProtectedAppScanSpeeds;

    public MonitoredFolder? SelectedRecordFolder
    {
        get => _selectedRecordFolder;
        set
        {
            if (SetProperty(ref _selectedRecordFolder, value))
            {
                _removeRecordFolderCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public MonitoredFolder? SelectedLockedFolder
    {
        get => _selectedLockedFolder;
        set
        {
            if (SetProperty(ref _selectedLockedFolder, value))
            {
                _removeLockedFolderCommand.RaiseCanExecuteChanged();
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

    public ProtectedAppProtectionModeOption SelectedProtectedAppProtectionMode
    {
        get => _selectedProtectedAppProtectionMode;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedProtectedAppProtectionMode, value))
            {
                SaveTextSettingAsync("options.protected_app_mode", value.Mode.ToString());
                OnPropertyChanged(nameof(ProtectedAppProtectionModeDescription));
            }
        }
    }

    public string ProtectedAppProtectionModeDescription => SelectedProtectedAppProtectionMode.Description;

    public ProtectedAppScanSpeedOption SelectedProtectedAppScanSpeed
    {
        get => _selectedProtectedAppScanSpeed;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedProtectedAppScanSpeed, value))
            {
                SaveTextSettingAsync("options.protected_app_scan_speed", value.Speed.ToString());
                OnPropertyChanged(nameof(ProtectedAppScanSpeedDescription));
            }
        }
    }

    public string ProtectedAppScanSpeedDescription => SelectedProtectedAppScanSpeed.Description;

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

    public bool RestoreProtectionAfterRestartEnabled
    {
        get => _restoreProtectionAfterRestartEnabled;
        set
        {
            if (SetProperty(ref _restoreProtectionAfterRestartEnabled, value))
            {
                SaveBoolSettingAsync("options.restore_protection_after_restart", value);
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

    public string ProtectedAppStatusMessage
    {
        get => _protectedAppStatusMessage;
        private set => SetProperty(ref _protectedAppStatusMessage, value);
    }

    public bool HasPcUsageWarning
    {
        get => _hasPcUsageWarning;
        private set
        {
            if (SetProperty(ref _hasPcUsageWarning, value))
            {
                OnPropertyChanged(nameof(PcUsageLogButtonText));
            }
        }
    }

    public string PcUsageLogButtonText => HasPcUsageWarning ? "PC 사용 기록 !" : "PC 사용 기록";

    public ICommand AddRecordFolderCommand { get; }

    public ICommand AddLockedFolderCommand { get; }

    public ICommand RemoveRecordFolderCommand => _removeRecordFolderCommand;

    public ICommand RemoveLockedFolderCommand => _removeLockedFolderCommand;

    public ICommand StartProtectionCommand => _toggleProtectionCommand;

    public ICommand RecentReportCommand { get; }

    public ICommand PcUsageLogCommand { get; }

    public ICommand HideWindowCommand { get; }

    public ICommand AddKakaoTalkCommand { get; }

    public ICommand AddNateOnCommand { get; }

    public ICommand AddRunningAppCommand { get; }


    public ICommand RemoveProtectedAppCommand => _removeProtectedAppCommand;

    public ICommand ChangePinCommand { get; }

    public ICommand ResetHotkeyCommand { get; }

    public event EventHandler? ProtectionStarted;

    public event EventHandler? StopProtectionRequested;

    public event EventHandler? OpenLatestReportRequested;

    public event EventHandler? OpenPcUsageLogRequested;

    public event EventHandler? HideWindowRequested;

    public event EventHandler? PinChangeRequested;

    public event EventHandler? HotkeyOptionsChanged;

    public event EventHandler<string>? UserMessageRequested;

    public async Task LoadAsync()
    {
        RecordFolders.Clear();
        LockedFolders.Clear();
        foreach (var folder in await _database.GetFoldersAsync())
        {
            if (folder.Kind == MonitoredFolderKind.Locked)
            {
                LockedFolders.Add(folder);
            }
            else
            {
                RecordFolders.Add(folder);
            }
        }

        await LoadProtectedAppsAsync();
        await LoadOptionsAsync();
        await RefreshPcUsageWarningAsync();
        RefreshProtectionState(_protection.IsProtectionActive);
        IsStartWithWindowsEnabled = _startupRegistration.IsEnabled();
    }

    public void RefreshProtectionState(bool isActive)
    {
        IsProtectionActive = isActive;
    }

    private async Task AddFolderAsync(MonitoredFolderKind kind)
    {
        var folder = _folderPicker.PickFolder();
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        await _database.AddFolderAsync(folder, kind);
        await LoadAsync();
    }

    private async Task RemoveSelectedRecordFolderAsync()
    {
        if (SelectedRecordFolder is null)
        {
            return;
        }

        await _database.RemoveFolderAsync(SelectedRecordFolder.Id);
        SelectedRecordFolder = null;
        await LoadAsync();
    }

    private async Task RemoveSelectedLockedFolderAsync()
    {
        if (SelectedLockedFolder is null)
        {
            return;
        }

        await _database.RemoveFolderAsync(SelectedLockedFolder.Id);
        SelectedLockedFolder = null;
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
        var result = await _protection.StartProtectionAsync(
            RecordFolders.Select(folder => folder.Path).ToArray(),
            LockedFolders.Select(folder => folder.Path).ToArray(),
            LockWorkstationOnProtectionStart,
            BlockProtectedAppsEnabled,
            SelectedProtectedAppProtectionMode.Mode,
            SelectedProtectedAppScanSpeed.Speed);
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
        (AddRecordFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AddLockedFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
        _removeRecordFolderCommand.RaiseCanExecuteChanged();
        _removeLockedFolderCommand.RaiseCanExecuteChanged();
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

        ProtectedAppStatusMessage = ProtectedApps.Count == 0
            ? "실행 중인 앱을 선택하면 이 목록에 저장됩니다."
            : $"저장된 보호 앱 {ProtectedApps.Count}개";
    }

    private async Task AddProtectedAppAsync(string displayName, string processName, string? executablePath)
    {
        await _database.AddProtectedAppAsync(displayName, processName, executablePath);
        await LoadProtectedAppsAsync();
        ProtectedAppStatusMessage = $"저장됨: {displayName}";
    }

    private async Task AddRunningAppAsync()
    {
        var candidate = _protectedAppPicker.PickRunningApp();
        if (candidate is null)
        {
            return;
        }

        await AddProtectedAppAsync(candidate.DisplayName, candidate.ProcessName, candidate.ExecutablePath);
    }

    private async Task AddNateOnPresetAsync()
    {
        await _database.AddProtectedAppAsync("네이트온", "NateOnMain", null);
        await _database.AddProtectedAppAsync("네이트온", "NateOn", null);
        await LoadProtectedAppsAsync();
        ProtectedAppStatusMessage = "저장됨: 네이트온";
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
        ProtectedAppStatusMessage = "선택한 앱을 삭제했습니다.";
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
            RestoreProtectionAfterRestartEnabled = await GetBoolSettingAsync("options.restore_protection_after_restart", defaultValue: false);
            LockWorkstationOnProtectionStart = await GetBoolSettingAsync("options.lock_workstation_on_start", defaultValue: true);
            HotkeyEnabled = await GetBoolSettingAsync("options.hotkey_enabled", defaultValue: true);
            HotkeyText = await _database.GetSettingAsync("options.hotkey_text") ?? DefaultHotkeyText;
            SelectedProtectedAppProtectionMode = FindProtectedAppModeOption(
                await _database.GetSettingAsync("options.protected_app_mode"));
            SelectedProtectedAppScanSpeed = FindProtectedAppScanSpeedOption(
                await _database.GetSettingAsync("options.protected_app_scan_speed"));
        }
        finally
        {
            _isLoadingOptions = false;
        }
    }

    public async Task RefreshPcUsageWarningAsync()
    {
        var start = await _database.GetSettingAsync("pc_usage.standard_start") ?? "09:00";
        var end = await _database.GetSettingAsync("pc_usage.standard_end") ?? "18:00";
        if (!PcUsageSchedule.TryCreate(start, end, out var schedule))
        {
            HasPcUsageWarning = false;
            return;
        }

        var events = await _database.GetPcUsageEventsAsync(DateTimeOffset.Now.AddDays(-14), 200);
        HasPcUsageWarning = events.Any(item => schedule.IsOutside(item.Timestamp));
    }

    private static ProtectedAppProtectionModeOption FindProtectedAppModeOption(string? value)
    {
        if (Enum.TryParse<ProtectedAppProtectionMode>(value, ignoreCase: true, out var mode))
        {
            return DefaultProtectedAppProtectionModes.FirstOrDefault(option => option.Mode == mode)
                ?? DefaultProtectedAppProtectionModes[1];
        }

        return DefaultProtectedAppProtectionModes[1];
    }

    private static ProtectedAppScanSpeedOption FindProtectedAppScanSpeedOption(string? value)
    {
        if (Enum.TryParse<ProtectedAppScanSpeed>(value, ignoreCase: true, out var speed))
        {
            return DefaultProtectedAppScanSpeeds.FirstOrDefault(option => option.Speed == speed)
                ?? DefaultProtectedAppScanSpeeds[0];
        }

        return DefaultProtectedAppScanSpeeds[0];
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
