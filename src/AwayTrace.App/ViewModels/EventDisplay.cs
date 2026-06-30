using AwayTrace.Core.Models;

namespace AwayTrace.App.ViewModels;

public static class EventDisplay
{
    public static string ToKorean(FileEventType type)
    {
        return type switch
        {
            FileEventType.Created => "파일 생성",
            FileEventType.Changed => "파일 수정",
            FileEventType.Deleted => "파일 삭제",
            FileEventType.Renamed => "파일 이름 변경",
            FileEventType.System => "시스템",
            FileEventType.WatcherError => "시스템 오류",
            _ => type.ToString()
        };
    }
}
