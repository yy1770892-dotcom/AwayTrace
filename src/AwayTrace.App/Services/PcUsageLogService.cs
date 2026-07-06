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
        var windowsEvents = await ReadWindowsSystemEventsAsync(from);

        return localEvents
            .Concat(windowsEvents)
            .OrderByDescending(item => item.Timestamp)
            .Take(300)
            .ToArray();
    }

    private static async Task<IReadOnlyList<PcUsageEvent>> ReadWindowsSystemEventsAsync(DateTimeOffset from)
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

            // stdout/stderr를 모두 비동기로 읽는다. stderr를 읽지 않으면
            // 버퍼가 가득 찼을 때 wevtutil이 멈추고 ReadToEnd가 영원히
            // 블록될 수 있다. 전체 작업에 5초 타임아웃을 건다.
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                return [];
            }

            var output = await outputTask;
            await errorTask;
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
        // wevtutil /f:xml 는 <Event>...</Event> 조각들을 바깥 루트 없이 이어붙여 출력한다.
        // 이벤트가 2개 이상이면 루트가 여러 개라 XDocument.Parse가 실패하므로
        // <Events>로 감싸 단일 루트로 만든 뒤 파싱한다. (앞쪽 BOM/공백도 제거)
        var trimmed = xml.Trim();
        var firstTag = trimmed.IndexOf('<');
        if (firstTag < 0)
        {
            return [];
        }

        if (firstTag > 0)
        {
            trimmed = trimmed[firstTag..];
        }

        XDocument document;
        try
        {
            document = XDocument.Parse("<Events>" + trimmed + "</Events>");
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
        return document.Descendants(ns + "Event")
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
            6005 => (PcUsageEventType.SystemStarted, "이벤트 로그 서비스 시작 기준 추정. 절전 복귀는 포함되지 않을 수 있음"),
            6006 => (PcUsageEventType.SystemShutdown, "이벤트 로그 서비스 중지 기준 추정. 절전 진입은 포함되지 않을 수 있음"),
            6008 => (PcUsageEventType.UnexpectedShutdown, "컴퓨터가 정상 종료되지 않은 기록"),
            _ => (PcUsageEventType.Unknown, "Windows 시스템 기록")
        };

        return new PcUsageEvent(0, timestamp.ToLocalTime(), type, description, "Windows 이벤트 로그");
    }
}
