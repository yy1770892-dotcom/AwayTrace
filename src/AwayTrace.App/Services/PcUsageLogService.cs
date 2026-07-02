using System.Diagnostics;
using System.Xml.Linq;
using AwayTrace.Core.Models;
using AwayTrace.Core.Storage;

namespace AwayTrace.App.Services;

public sealed class PcUsageLogService
{
    private readonly AwayTraceDatabase _database;

    public PcUsageLogService(AwayTraceDatabase database)
    {
        _database = database;
    }

    public Task RecordAsync(PcUsageEventType eventType, string description)
    {
        return _database.AddPcUsageEventAsync(new PcUsageEvent(
            0,
            DateTimeOffset.Now,
            eventType,
            description,
            "AwayTrace"));
    }

    public async Task<IReadOnlyList<PcUsageEvent>> GetRecentEventsAsync(DateTimeOffset from)
    {
        var localEvents = await _database.GetPcUsageEventsAsync(from, 200);
        var windowsEvents = await Task.Run(() => ReadWindowsSystemEvents(from));

        return localEvents
            .Concat(windowsEvents)
            .OrderByDescending(item => item.Timestamp)
            .Take(300)
            .ToArray();
    }

    private static IReadOnlyList<PcUsageEvent> ReadWindowsSystemEvents(DateTimeOffset from)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wevtutil.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("qe");
            startInfo.ArgumentList.Add("System");
            startInfo.ArgumentList.Add("/q:*[System[(EventID=6005 or EventID=6006 or EventID=6008)]]");
            startInfo.ArgumentList.Add("/f:xml");
            startInfo.ArgumentList.Add("/c:80");
            startInfo.ArgumentList.Add("/rd:true");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return [];
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(4000);
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return [];
            }

            return ParseWindowsEvents(output, from);
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<PcUsageEvent> ParseWindowsEvents(string xml, DateTimeOffset from)
    {
        var document = XDocument.Parse(xml);
        XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
        IEnumerable<XElement> events = document.Root?.Name == ns + "Event"
            ? new[] { document.Root }
            : document.Descendants(ns + "Event");

        return events
            .Select(element => TryReadWindowsEvent(element, ns))
            .Where(item => item is not null && item.Timestamp >= from)
            .Select(item => item!)
            .ToArray();
    }

    private static PcUsageEvent? TryReadWindowsEvent(XElement eventElement, XNamespace ns)
    {
        var system = eventElement.Element(ns + "System");
        var idText = system?.Element(ns + "EventID")?.Value;
        var timeText = system?.Element(ns + "TimeCreated")?.Attribute("SystemTime")?.Value;
        if (!int.TryParse(idText, out var id) || !DateTimeOffset.TryParse(timeText, out var timestamp))
        {
            return null;
        }

        var (type, description) = id switch
        {
            6005 => (PcUsageEventType.SystemStarted, "컴퓨터 켜진 시간으로 추정"),
            6006 => (PcUsageEventType.SystemShutdown, "컴퓨터 꺼진 시간으로 추정"),
            6008 => (PcUsageEventType.UnexpectedShutdown, "컴퓨터가 정상 종료되지 않은 기록"),
            _ => (PcUsageEventType.Unknown, "Windows 시스템 기록")
        };

        return new PcUsageEvent(0, timestamp.ToLocalTime(), type, description, "Windows 이벤트 로그");
    }
}
