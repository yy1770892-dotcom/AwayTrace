namespace AwayTrace.Core.Services;

public sealed record PcUsageSchedule(TimeOnly Start, TimeOnly End)
{
    public bool IsOutside(DateTimeOffset timestamp)
    {
        var time = TimeOnly.FromDateTime(timestamp.LocalDateTime);
        if (Start == End)
        {
            return false;
        }

        if (Start < End)
        {
            return time < Start || time > End;
        }

        return time > End && time < Start;
    }

    public static bool TryCreate(string startText, string endText, out PcUsageSchedule schedule)
    {
        if (TimeOnly.TryParse(startText, out var start) && TimeOnly.TryParse(endText, out var end))
        {
            schedule = new PcUsageSchedule(start, end);
            return true;
        }

        schedule = new PcUsageSchedule(new TimeOnly(9, 0), new TimeOnly(18, 0));
        return false;
    }
}
