using System.Windows;
using AwayTrace.App.Services;
using AwayTrace.App.ViewModels;
using AwayTrace.App.Views;
using AwayTrace.Core.Services;
using AwayTrace.Core.Storage;
using Microsoft.Win32;

namespace AwayTrace.App;

public partial class App : System.Windows.Application
{
    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;
    private AwayTraceDatabase? _database;
    private PinService? _pinService;
    private FileMonitorService? _fileMonitor;
    private AppBlockerService? _appBlocker;
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
            _instanceMutex = new Mutex(initiallyOwned: true, "AwayTrace.SingleInstance", out var createdNew);
            if (!createdNew)
            {
                System.Windows.MessageBox.Show(
                    "AwayTrace가 이미 실행 중입니다. 작업 표시줄의 숨겨진 아이콘 영역에서 AwayTrace를 열어 주세요.",
                    "AwayTrace",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            _ownsInstanceMutex = true;

            _database = new AwayTraceDatabase();
            _database.Initialize();

            var recoveredCount = await new SessionRecoveryService(_database)
                .RecoverAbandonedSessionsAsync(DateTimeOffset.Now);

            _pinService = new PinService(_database);
            if (!await _pinService.HasPinAsync())
            {
                ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
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
            }
            catch (Exception startupEx)
            {
                System.Windows.MessageBox.Show(
                    $"Windows 자동 실행을 등록하지 못했습니다. 앱은 계속 실행됩니다.\n{startupEx.Message}",
                    "AwayTrace",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            _fileMonitor = new FileMonitorService(_database);
            _appBlocker = new AppBlockerService(_database);
            _folderLock = new FolderLockService(_database);
            _protection = new ProtectionCoordinator(_database, _fileMonitor, _appBlocker, _folderLock, new WorkstationLockService());
            _trayIcon = new TrayIconService();
            _trayIcon.ShowRequested += (_, _) => Dispatcher.Invoke(ShowMainWindow);
            _trayIcon.StopProtectionRequested += (_, _) => InvokeOnUiAsync(StopProtectionFromTrayAsync);
            _trayIcon.ExitRequested += (_, _) => InvokeOnUiAsync(ExitFromTrayAsync);

            SystemEvents.SessionSwitch += OnSessionSwitch;

            _mainViewModel = new MainViewModel(
                _database,
                new FolderPickerService(),
                new ProtectedAppPickerService(),
                _protection,
                _startupRegistration);
            _mainViewModel.UserMessageRequested += (_, message) => System.Windows.MessageBox.Show(message, "AwayTrace", MessageBoxButton.OK, MessageBoxImage.Information);
            _mainViewModel.ProtectionStarted += (_, _) =>
            {
                _trayIcon.SetProtectionActive(true);
                if (_mainViewModel.LockWorkstationOnProtectionStart)
                {
                    _mainWindow?.Hide();
                }
            };
            _mainViewModel.StopProtectionRequested += (_, _) => InvokeOnUiAsync(async () =>
            {
                await PromptAndStopProtectionAsync(openReport: true, shutdownAfter: false);
            });
            _mainViewModel.OpenLatestReportRequested += async (_, _) => await ShowLatestReportAsync();
            _mainViewModel.PinChangeRequested += (_, _) => ShowPinChangeWindow();
            _mainViewModel.HotkeyOptionsChanged += (_, _) => ConfigureHotkey();
            await _mainViewModel.LoadAsync();

            _mainWindow = new MainWindow(_mainViewModel);
            MainWindow = _mainWindow;
            ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
            _hotkeyService = new GlobalHotkeyService();
            _hotkeyService.Bind(_mainWindow, () => Dispatcher.Invoke(StartProtectionFromHotkey));
            ConfigureHotkey();
            if (!e.Args.Contains("--autostart", StringComparer.OrdinalIgnoreCase))
            {
                _mainWindow.Show();
            }

            if (recoveredCount > 0)
            {
                System.Windows.MessageBox.Show(
                    "이전 보호 세션이 정상 종료되지 않아 기록 신뢰도 낮음으로 표시했습니다.",
                    "AwayTrace",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"AwayTrace를 시작할 수 없습니다.\n{ex.Message}", "AwayTrace", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _hotkeyService?.Dispose();
        _trayIcon?.Dispose();
        _appBlocker?.Dispose();
        _fileMonitor?.Dispose();
        if (_ownsInstanceMutex)
        {
            _instanceMutex?.ReleaseMutex();
        }

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = System.Windows.WindowState.Normal;
        _mainWindow.Activate();
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

    private async Task StopProtectionFromTrayAsync()
    {
        await PromptAndStopProtectionAsync(openReport: true, shutdownAfter: false);
    }

    private void StartProtectionFromHotkey()
    {
        if (_mainViewModel is null || _mainViewModel.IsProtectionActive)
        {
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
            System.Windows.MessageBox.Show(error, "AwayTrace 단축키", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            System.Windows.MessageBox.Show("PIN이 변경되었습니다.", "AwayTrace", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async Task ExitFromTrayAsync()
    {
        if (_protection?.IsProtectionActive == true)
        {
            System.Windows.MessageBox.Show(
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
            System.Windows.MessageBox.Show("아직 생성된 리포트가 없습니다.", "AwayTrace", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private async void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (_protection is null)
        {
            return;
        }

        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            await _protection.RecordWindowsSessionEventAsync("Windows 세션 잠금");
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            await _protection.RecordWindowsSessionEventAsync("Windows 세션 잠금 해제");
        }
    }
}
