using AwayTrace.Core.Models;
using AwayTrace.Core.Services;

namespace AwayTrace.App.ViewModels;

public sealed class PcUsageEventRow
{
    public PcUsageEventRow(PcUsageEvent usageEvent, PcUsageSchedule schedule)
    {
        TimeText = usageEvent.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        TypeText = usageEvent.EventType switch
        {
            PcUsageEventType.AppStarted => "AwayTrace 실행 시간",
            PcUsageEventType.AppExited => "AwayTrace 종료 시간",
            PcUsageEventType.SessionLocked => "Windows 잠금 시간",
            PcUsageEventType.SessionUnlocked => "Windows 잠금 해제 시간",
            PcUsageEventType.SystemStarted => "컴퓨터 켜진 시간",
            PcUsageEventType.SystemShutdown => "컴퓨터 꺼진 시간",
            PcUsageEventType.UnexpectedShutdown => "비정상 종료 시간",
            _ => "시스템 기록 시간"
        };
        DetailText = usageEvent.Description;
        SourceText = usageEvent.Source;
        OutsideHoursText = schedule.IsOutside(usageEvent.Timestamp) ? "규격 외 사용 시간" : string.Empty;
    }

    public string TimeText { get; }

    public string TypeText { get; }

    public string DetailText { get; }

    public string SourceText { get; }

    public string OutsideHoursText { get; }
}
