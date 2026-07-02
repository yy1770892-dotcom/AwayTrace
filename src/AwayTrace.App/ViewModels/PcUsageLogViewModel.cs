using System.Collections.ObjectModel;
using System.Windows.Input;
using AwayTrace.App.Services;
using AwayTrace.Core.Services;
using AwayTrace.Core.Storage;

namespace AwayTrace.App.ViewModels;

public sealed class PcUsageLogViewModel : ObservableObject
{
    private readonly AwayTraceDatabase _database;
    private readonly PcUsageLogService _usageLogService;
    private string _standardStartText = "09:00";
    private string _standardEndText = "18:00";
    private string _statusText = "최근 PC 사용 기록을 불러옵니다.";
    private bool _hasOutsideHours;

    public PcUsageLogViewModel(AwayTraceDatabase database, PcUsageLogService usageLogService)
    {
        _database = database;
        _usageLogService = usageLogService;
        RefreshCommand = new RelayCommand(LoadAsync);
        SaveStandardHoursCommand = new RelayCommand(SaveStandardHoursAsync);
    }

    public ObservableCollection<PcUsageEventRow> Events { get; } = [];

    public string StandardStartText
    {
        get => _standardStartText;
        set => SetProperty(ref _standardStartText, value.Trim());
    }

    public string StandardEndText
    {
        get => _standardEndText;
        set => SetProperty(ref _standardEndText, value.Trim());
    }

    public bool HasOutsideHours
    {
        get => _hasOutsideHours;
        private set => SetProperty(ref _hasOutsideHours, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public ICommand RefreshCommand { get; }

    public ICommand SaveStandardHoursCommand { get; }

    public async Task LoadAsync()
    {
        StandardStartText = await _database.GetSettingAsync("pc_usage.standard_start") ?? "09:00";
        StandardEndText = await _database.GetSettingAsync("pc_usage.standard_end") ?? "18:00";
        await LoadEventsAsync();
    }

    private async Task SaveStandardHoursAsync()
    {
        if (!PcUsageSchedule.TryCreate(StandardStartText, StandardEndText, out _))
        {
            StatusText = "시간은 09:00 형식으로 입력해 주세요.";
            return;
        }

        await _database.SetSettingAsync("pc_usage.standard_start", StandardStartText);
        await _database.SetSettingAsync("pc_usage.standard_end", StandardEndText);
        await LoadEventsAsync();
        StatusText = "표준 사용 시간을 저장했습니다.";
    }

    private async Task LoadEventsAsync()
    {
        PcUsageSchedule.TryCreate(StandardStartText, StandardEndText, out var schedule);
        var from = DateTimeOffset.Now.AddDays(-14);
        var events = await _usageLogService.GetRecentEventsAsync(from);

        Events.Clear();
        foreach (var usageEvent in events)
        {
            Events.Add(new PcUsageEventRow(usageEvent, schedule));
        }

        HasOutsideHours = events.Any(item => schedule.IsOutside(item.Timestamp));
        StatusText = HasOutsideHours
            ? "규격 외 사용 시간이 있습니다."
            : "규격 외 사용 시간이 없습니다.";
    }
}
