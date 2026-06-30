using AwayTrace.Core.Models;

namespace AwayTrace.App.ViewModels;

public sealed class ReportEventRow
{
    public ReportEventRow(FileEventRecord source)
    {
        Source = source;
    }

    public FileEventRecord Source { get; }

    public string TimeText => Source.Timestamp.ToLocalTime().ToString("HH:mm");

    public string TypeText => EventDisplay.ToKorean(Source.EventType);

    public string DetailText => Source.EventType == FileEventType.Renamed && !string.IsNullOrWhiteSpace(Source.OldPath)
        ? $"{Source.OldPath} -> {Source.Path}"
        : Source.Path;
}
