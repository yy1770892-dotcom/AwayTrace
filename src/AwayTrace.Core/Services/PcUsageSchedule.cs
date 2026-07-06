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
        if (TryParseTime(startText, out var start) && TryParseTime(endText, out var end))
        {
            schedule = new PcUsageSchedule(start, end);
            return true;
        }

        schedule = new PcUsageSchedule(new TimeOnly(9, 0), new TimeOnly(18, 0));
        return false;
    }

    /// <summary>
    /// "09:00"뿐 아니라 콜론 없는 숫자 입력("0900", "900", "9")도 받아 시각으로 해석한다.
    /// </summary>
    public static bool TryParseTime(string? text, out TimeOnly time)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length > 0 && value.All(char.IsDigit))
        {
            value = value.Length switch
            {
                1 or 2 => value + ":00",          // "9" -> 9:00, "09" -> 09:00
                3 => value[..1] + ":" + value[1..], // "900" -> 9:00
                4 => value[..2] + ":" + value[2..], // "0900" -> 09:00
                _ => value
            };
        }

        return TimeOnly.TryParse(value, out time);
    }

    /// <summary>입력된 시각 문자열을 "HH:mm" 표준 형태로 정규화한다(실패 시 원본 반환).</summary>
    public static string Normalize(string? text)
    {
        return TryParseTime(text, out var time) ? time.ToString("HH:mm") : (text ?? string.Empty);
    }
}
