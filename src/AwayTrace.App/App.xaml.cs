using System.Windows;
using AwayTrace.App.Services;
using AwayTrace.App.ViewModels;
using AwayTrace.App.Views;
using AwayTrace.Core.Models;
using AwayTrace.Core.Services;
using AwayTrace.Core.Storage;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;

namespace AwayTrace.App;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = "AwayTrace.SingleInstance";
    private const string ShowWindowEventName = "AwayTrace.ShowMainWindow";

    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;
    private EventWaitHandle? _showWindowEvent;
    private Thread? _showWindowThread;
    private volatile bool _isExiting;
    private AwayTraceDatabase? _database;
    private PinService? _pinService;
    private FileMonitorService? _fileMonitor;
    private AppBlockerService? _appBlocker;
    private PcUsageLogService? _pcUsageLog;
    private FolderLockService? _folderLock;
    private ProtectionCoordinator? _protection;
    private StartupRegistrationService? _startupRegistration;
    private GlobalHotkeyService? _hotkeyService;
    private TrayIconService? _trayIcon;
    private MainViewModel? _mainViewModel;
    private MainWindow? _mainWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _instanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
            if (!createdNew)
            {
                SignalExistingInstanceToShowWindow();
                Shutdown();
                return;
            }

            _ownsInstanceMutex = true;
            StartShowWindowSignalListener();

            _database = new AwayTraceDatabase();
            _database.Initialize();

            _pinService = new PinService(_database);
            if (!await _pinService.HasPinAsync())
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var setupWindow = new PinSetupWindow(_pinService);
                if (setupWindow.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }

            _startupRegistration = new StartupRegistrationService();
            try
            {
                await EnableStartupOnceAfterUserApprovalAsync(_database, _startupRegistration);
                _startupRegistration.RefreshIfEnabled();
            }
            catch (Exception startupEx)
            {
                MessageBox.Show(
                    $"Windows 자동 실행을 등록하지 못했습니다. 앱은 계속 실행됩니다.\n{startupEx.Message}",
                    "AwayTrace",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            _fileMonitor = new FileMonitorService(_database);
            _appBlocker = new AppBlockerService(_database);
            _pcUsageLog = new PcUsageLogService(_database);
            await _pcUsageLog.RecordAsync(PcUsageEventType.AppStarted, "AwayTrace 실행");
            _folderLock = new FolderLockService(_database);
            _protection = new ProtectionCoordinator(_database, _fileMonitor, _appBlocker, _folderLock, new WorkstationLockService());
            _trayIcon = new TrayIconService();
            _trayIcon.ShowRequested += (_, _) => Dispatcher.Invoke(ShowMainWindow);
            _trayIcon.StopProtectionRequested += (_, _) => InvokeOnUiAsync(StopProtectionFromTrayAsync);
            _trayIcon.ExitRequested += (_, _) => InvokeOnUiAsync(ExitFromTrayAsync);

            var startupState = await HandleStartupProtectionStateAsync(_database, _protection, _folderLock);

            SystemEvents.SessionSwitch += OnSessionSwitch;

            _mainViewModel = new MainViewModel(
                _database,
                new FolderPickerService(),
                new ProtectedAppPickerService(),
                _protection,
                _startupRegistration);
            _mainViewModel.UserMessageRequested += (_, message) => MessageBox.Show(message, "AwayTrace", MessageBoxButton.OK, MessageBoxImage.Information);
            _mainViewModel.HideWindowRequested += (_, _) => HideMainWindow();
            _mainViewModel.ProtectionStarted += (_, _) =>
            {
                _trayIcon.SetProtectionActive(true);
                _trayIcon.ShowInfo(
                    "AwayTrace 보호 중",
                    "복귀 후 Ctrl+Alt+A 또는 AwayTrace 재실행으로 창을 다시 열 수 있습니다.");
                if (_mainViewModel.LockWorkstationOnProtectionStart)
                {
                    HideMainWindow();
                }
            };
            _mainViewModel.StopProtectionRequested += (_, _) => InvokeOnUiAsync(async () =>
            {
                await PromptAndStopProtectionAsync(openReport: true, shutdownAfter: false);
            });
            _mainViewModel.OpenLatestReportRequested += async (_, _) => await ShowLatestReportAsync();
            _mainViewModel.OpenPcUsageLogRequested += async (_, _) => await ShowPcUsageLogAsync();
            _mainViewModel.PinChangeRequested += (_, _) => ShowPinChangeWindow();
            _mainViewModel.HotkeyOptionsChanged += (_, _) => ConfigureHotkey();
            await _mainViewModel.LoadAsync();

            _mainWindow = new MainWindow(_mainViewModel);
            MainWindow = _mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            _hotkeyService = new GlobalHotkeyService();
            _hotkeyService.Bind(_mainWindow, () => Dispatcher.Invoke(HandleHotkey));
            ConfigureHotkey();

            _trayIcon.SetProtectionActive(_protection.IsProtectionActive);
            if (startupState.RestoredProtection)
            {
                _trayIcon.ShowInfo(
                    "AwayTrace 보호 복구",
                    "재부팅 후 이전 보호 모드를 다시 적용했습니다. 리포트에는 기록 공백 가능성이 표시됩니다.");
            }

            if (!e.Args.Contains("--autostart", StringComparer.OrdinalIgnoreCase))
            {
                _mainWindow.Show();
            }

            if (startupState.RecoveredAbnormalSessions > 0)
            {
                MessageBox.Show(
                    "이전 보호 세션이 정상 종료되지 않아 기록 신뢰도 낮음으로 표시했습니다.",
                    "AwayTrace",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"AwayTrace를 시작할 수 없습니다.\n{ex.Message}", "AwayTrace", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _isExiting = true;
        _showWindowEvent?.Set();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _hotkeyService?.Dispose();
        _trayIcon?.Dispose();
        _appBlocker?.Dispose();
        _fileMonitor?.Dispose();
        try
        {
            _pcUsageLog?.RecordAsync(PcUsageEventType.AppExited, "AwayTrace 종료").GetAwaiter().GetResult();
        }
        catch
        {
        }

        _showWindowEvent?.Dispose();
        if (_ownsInstanceMutex)
        {
            _instanceMutex?.ReleaseMutex();
        }

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void SignalExistingInstanceToShowWindow()
    {
        try
        {
            using var showWindowEvent = EventWaitHandle.OpenExisting(ShowWindowEventName);
            showWindowEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            MessageBox.Show(
                "AwayTrace가 이미 실행 중이지만 창 열기 신호를 보낼 수 없습니다. 작업 관리자에서 기존 AwayTrace를 종료한 뒤 다시 실행해 주세요.",
                "AwayTrace",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void StartShowWindowSignalListener()
    {
        _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
        _showWindowThread = new Thread(() =>
        {
            while (!_isExiting)
            {
                _showWindowEvent.WaitOne();
                if (_isExiting)
                {
                    return;
                }

                Dispatcher.Invoke(ShowMainWindow);
            }
        })
        {
            IsBackground = true,
            Name = "AwayTrace show-window listener"
        };
        _showWindowThread.Start();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _trayIcon?.SetProtectionActive(_mainViewModel?.IsProtectionActive == true);
    }

    private void HideMainWindow()
    {
        _mainWindow?.Hide();
        _trayIcon?.ShowInfo("AwayTrace 창 숨김", "Ctrl+Alt+A 또는 AwayTrace 재실행으로 다시 열 수 있습니다.");
    }

    private void InvokeOnUiAsync(Func<Task> action)
    {
        _ = Dispatcher.InvokeAsync(() => _ = action());
    }

    private static async Task EnableStartupOnceAfterUserApprovalAsync(
        AwayTraceDatabase database,
        StartupRegistrationService startupRegistration)
    {
        const string key = "startup.auto_start_default_applied";
        if (await database.GetSettingAsync(key) == "1")
        {
            return;
        }

        startupRegistration.Enable();
        await database.SetSettingAsync(key, "1");
    }

    private static async Task<StartupProtectionState> HandleStartupProtectionStateAsync(
        AwayTraceDatabase database,
        ProtectionCoordinator protection,
        FolderLockService folderLock)
    {
        var activeSessions = await database.GetActiveSessionsAsync();
        if (activeSessions.Count == 0)
        {
            return new StartupProtectionState(0, false);
        }

        var restoreEnabled = await GetBoolSettingAsync(database, "options.restore_protection_after_restart", defaultValue: false);
        if (!restoreEnabled)
        {
            await UnlockFoldersFromActiveSessionsAsync(database, folderLock);
            var recoveredCount = await new SessionRecoveryService(database)
                .RecoverAbandonedSessionsAsync(DateTimeOffset.Now);
            return new StartupProtectionState(recoveredCount, false);
        }

        var sessionToRestore = activeSessions
            .OrderByDescending(session => session.StartedAt)
            .First();
        var olderSessions = activeSessions
            .Where(session => session.Id != sessionToRestore.Id)
            .ToArray();
        foreach (var olderSession in olderSessions)
        {
            await database.MarkSessionAbnormalAsync(
                olderSession.Id,
                DateTimeOffset.Now,
                "새 보호 세션 자동 복구 전에 이전 활성 세션을 정리했습니다.");
        }

        var protectApps = await GetBoolSettingAsync(database, "options.block_protected_apps", defaultValue: false);
        var protectedAppMode = await GetProtectedAppModeAsync(database);
        var protectedAppScanSpeed = await GetProtectedAppScanSpeedAsync(database);
        var restoreResult = await protection.ResumeActiveSessionAsync(
            sessionToRestore,
            protectApps,
            protectedAppMode,
            protectedAppScanSpeed);
        if (restoreResult.Success)
        {
            return new StartupProtectionState(olderSessions.Length, true);
        }

        await UnlockFoldersFromActiveSessionsAsync(database, folderLock);
        var recoveredAfterFailure = await new SessionRecoveryService(database)
            .RecoverAbandonedSessionsAsync(DateTimeOffset.Now);
        MessageBox.Show(
            $"이전 보호 모드를 자동 복구하지 못해 잠금 폴더를 안전 해제하고 세션을 비정상 종료로 표시했습니다.\n{restoreResult.ErrorMessage}",
            "AwayTrace",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return new StartupProtectionState(recoveredAfterFailure, false);
    }

    private static async Task UnlockFoldersFromActiveSessionsAsync(
        AwayTraceDatabase database,
        FolderLockService folderLock)
    {
        var activeSessions = await database.GetActiveSessionsAsync();
        foreach (var session in activeSessions)
        {
            var folders = ProtectionCoordinator.ParseLockedFolderSnapshot(session.FolderSnapshotJson);
            if (folders.Count > 0)
            {
                await folderLock.UnlockFoldersAsync(session.Id, folders);
            }
        }
    }

    private async Task StopProtectionFromTrayAsync()
    {
        await PromptAndStopProtectionAsync(openReport: true, shutdownAfter: false);
    }

    private void HandleHotkey()
    {
        if (_mainViewModel is null)
        {
            return;
        }

        if (_mainWindow is null || !_mainWindow.IsVisible || _mainWindow.WindowState == WindowState.Minimized || _mainViewModel.IsProtectionActive)
        {
            ShowMainWindow();
            return;
        }

        if (_mainViewModel.StartProtectionCommand.CanExecute(null))
        {
            _mainViewModel.StartProtectionCommand.Execute(null);
        }
    }

    private void ConfigureHotkey()
    {
        if (_hotkeyService is null || _mainViewModel is null)
        {
            return;
        }

        if (!_hotkeyService.Configure(_mainViewModel.HotkeyEnabled, _mainViewModel.HotkeyText, out var error)
            && !string.IsNullOrWhiteSpace(error))
        {
            MessageBox.Show(error, "AwayTrace 단축키", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowPinChangeWindow()
    {
        if (_pinService is null)
        {
            return;
        }

        var window = new PinChangeWindow(_pinService)
        {
            Owner = _mainWindow?.IsVisible == true ? _mainWindow : null
        };
        if (window.ShowDialog() == true)
        {
            MessageBox.Show("PIN이 변경되었습니다.", "AwayTrace", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async Task ExitFromTrayAsync()
    {
        if (_protection?.IsProtectionActive == true)
        {
            MessageBox.Show(
                "보호 중에는 PIN 인증 후 종료해야 합니다.",
                "AwayTrace",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            await PromptAndStopProtectionAsync(openReport: false, shutdownAfter: true);
            return;
        }

        if (_mainWindow is not null)
        {
            _mainWindow.AllowClose = true;
        }

        Shutdown();
    }

    private async Task<Guid?> PromptAndStopProtectionAsync(bool openReport, bool shutdownAfter)
    {
        if (_pinService is null || _protection is null)
        {
            return null;
        }

        var prompt = new PinPromptWindow(_pinService)
        {
            Owner = _mainWindow?.IsVisible == true ? _mainWindow : null
        };

        if (prompt.ShowDialog() != true)
        {
            return null;
        }

        var stoppedSessionId = await _protection.StopProtectionAsync();
        _trayIcon?.SetProtectionActive(false);
        _mainViewModel?.RefreshProtectionState(false);

        if (openReport && stoppedSessionId is Guid sessionId)
        {
            await ShowReportAsync(sessionId);
        }

        if (shutdownAfter)
        {
            if (_mainWindow is not null)
            {
                _mainWindow.AllowClose = true;
            }

            Shutdown();
        }

        return stoppedSessionId;
    }

    private async Task ShowLatestReportAsync()
    {
        if (_database is null)
        {
            return;
        }

        var latest = await _database.GetLatestSessionAsync();
        if (latest is null)
        {
            MessageBox.Show("아직 생성된 리포트가 없습니다.", "AwayTrace", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await ShowReportAsync(latest.Id);
    }

    private async Task ShowReportAsync(Guid sessionId)
    {
        if (_database is null)
        {
            return;
        }

        var session = await _database.GetSessionAsync(sessionId);
        if (session is null)
        {
            return;
        }

        var events = await _database.GetSessionEventsAsync(sessionId);
        var viewModel = new ReportViewModel(
            session,
            events,
            new ReportExportService(),
            new SaveFilePickerService());
        var window = new ReportWindow(viewModel)
        {
            Owner = _mainWindow?.IsVisible == true ? _mainWindow : null
        };
        viewModel.StopProtectionRequested += async (_, _) =>
        {
            var stoppedSessionId = await PromptAndStopProtectionAsync(openReport: true, shutdownAfter: false);
            if (stoppedSessionId is not null)
            {
                window.Close();
            }
        };
        window.Show();
    }

    private async Task ShowPcUsageLogAsync()
    {
        if (_database is null || _pcUsageLog is null)
        {
            return;
        }

        var viewModel = new PcUsageLogViewModel(_database, _pcUsageLog);
        await viewModel.LoadAsync();
        var window = new PcUsageLogWindow(viewModel)
        {
            Owner = _mainWindow?.IsVisible == true ? _mainWindow : null
        };
        window.Closed += async (_, _) =>
        {
            if (_mainViewModel is not null)
            {
                await _mainViewModel.RefreshPcUsageWarningAsync();
            }
        };
        window.Show();
    }

    private async void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (_protection is null)
        {
            return;
        }

        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            if (_pcUsageLog is not null)
            {
                await _pcUsageLog.RecordAsync(PcUsageEventType.SessionLocked, "Windows 세션 잠금");
            }

            await _protection.RecordWindowsSessionEventAsync("Windows 세션 잠금");
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            if (_pcUsageLog is not null)
            {
                await _pcUsageLog.RecordAsync(PcUsageEventType.SessionUnlocked, "Windows 세션 잠금 해제");
            }

            await _protection.RecordWindowsSessionEventAsync("Windows 세션 잠금 해제");
        }
    }

    private static async Task<bool> GetBoolSettingAsync(AwayTraceDatabase database, string key, bool defaultValue)
    {
        var value = await database.GetSettingAsync(key);
        return value is null ? defaultValue : value == "1";
    }

    private static async Task<ProtectedAppProtectionMode> GetProtectedAppModeAsync(AwayTraceDatabase database)
    {
        var value = await database.GetSettingAsync("options.protected_app_mode");
        return Enum.TryParse<ProtectedAppProtectionMode>(value, ignoreCase: true, out var mode)
            ? mode
            : ProtectedAppProtectionMode.HideWindows;
    }

    private static async Task<ProtectedAppScanSpeed> GetProtectedAppScanSpeedAsync(AwayTraceDatabase database)
    {
        var value = await database.GetSettingAsync("options.protected_app_scan_speed");
        return Enum.TryParse<ProtectedAppScanSpeed>(value, ignoreCase: true, out var speed)
            ? speed
            : ProtectedAppScanSpeed.Normal;
    }

    private sealed record StartupProtectionState(int RecoveredAbnormalSessions, bool RestoredProtection);
}
