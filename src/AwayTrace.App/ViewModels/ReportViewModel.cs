using System.Collections.ObjectModel;
using System.Windows.Input;
using AwayTrace.App.Services;
using AwayTrace.Core.Models;
using AwayTrace.Core.Services;

namespace AwayTrace.App.ViewModels;

public sealed class ReportViewModel : ObservableObject
{
    private readonly WatchSession _session;
    private readonly IReadOnlyList<FileEventRecord> _allEvents;
    private readonly ReportExportService _exportService;
    private readonly ISaveFilePickerService _saveFilePicker;
    private string _selectedFilter = "전체";

    public ReportViewModel(
        WatchSession session,
        IReadOnlyList<FileEventRecord> events,
        ReportExportService exportService,
        ISaveFilePickerService saveFilePicker)
    {
        _session = session;
        _allEvents = events;
        _exportService = exportService;
        _saveFilePicker = saveFilePicker;
        Events = [];
        ExportJsonCommand = new RelayCommand(ExportJson);
        ExportCsvCommand = new RelayCommand(ExportCsv);
        StopProtectionCommand = new RelayCommand(() => StopProtectionRequested?.Invoke(this, EventArgs.Empty), () => CanStopProtection);
        ApplyFilter();
    }

    public ObservableCollection<ReportEventRow> Events { get; }

    public IReadOnlyList<string> FilterOptions { get; } =
        ["전체", "생성", "수정", "삭제", "이름 변경", "시스템"];

    public string SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                ApplyFilter();
            }
        }
    }

    public string PeriodText
    {
        get
        {
            var start = _session.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            var end = _session.EndedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "진행 중";
            return $"{start} ~ {end}";
        }
    }

    public string StatusText
    {
        get
        {
            if (_session.LowConfidence || _session.State == ProtectionSessionState.AbnormalTermination)
            {
                return "기록 신뢰도 낮음";
            }

            return HasFileChangeEvents() ? "파일 변경 감지" : "파일 변경 없음";
        }
    }

    public string NoticeText =>
        "이 리포트는 파일 변경 정황을 보여주며, 행위자 식별이나 파일 열람 여부를 증명하지 않습니다.";

    public bool CanStopProtection => _session.State == ProtectionSessionState.Active && _session.EndedAt is null;

    public ICommand ExportJsonCommand { get; }

    public ICommand ExportCsvCommand { get; }

    public ICommand StopProtectionCommand { get; }

    public event EventHandler? StopProtectionRequested;

    private void ExportJson()
    {
        var path = _saveFilePicker.PickSaveFile(
            "JSON 리포트 내보내기",
            "JSON 파일 (*.json)|*.json",
            $"AwayTrace-{_session.StartedAt:yyyyMMdd-HHmmss}.json");
        if (path is not null)
        {
            _exportService.ExportJson(path, _session, _allEvents);
        }
    }

    private void ExportCsv()
    {
        var path = _saveFilePicker.PickSaveFile(
            "CSV 리포트 내보내기",
            "CSV 파일 (*.csv)|*.csv",
            $"AwayTrace-{_session.StartedAt:yyyyMMdd-HHmmss}.csv");
        if (path is not null)
        {
            _exportService.ExportCsv(path, _allEvents);
        }
    }

    private bool HasFileChangeEvents()
    {
        return _allEvents.Any(item => item.EventType is FileEventType.Created
            or FileEventType.Changed
            or FileEventType.Deleted
            or FileEventType.Renamed);
    }

    private void ApplyFilter()
    {
        Events.Clear();
        foreach (var item in _allEvents.Where(MatchesFilter))
        {
            Events.Add(new ReportEventRow(item));
        }
    }

    private bool MatchesFilter(FileEventRecord item)
    {
        return SelectedFilter switch
        {
            "생성" => item.EventType == FileEventType.Created,
            "수정" => item.EventType == FileEventType.Changed,
            "삭제" => item.EventType == FileEventType.Deleted,
            "이름 변경" => item.EventType == FileEventType.Renamed,
            "시스템" => item.EventType is FileEventType.System or FileEventType.WatcherError,
            _ => true
        };
    }
}
