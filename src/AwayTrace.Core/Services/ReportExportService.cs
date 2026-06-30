using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AwayTrace.Core.Models;

namespace AwayTrace.Core.Services;

public sealed class ReportExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public void ExportJson(string filePath, WatchSession session, IReadOnlyList<FileEventRecord> events)
    {
        var payload = new
        {
            notice = "이 리포트는 파일 변경 정황을 보여주며, 행위자 식별이나 파일 열람 여부를 증명하지 않습니다.",
            session = new
            {
                session.Id,
                session.StartedAt,
                session.EndedAt,
                State = session.State.ToString(),
                session.LowConfidence,
                session.AbnormalReason,
                session.FolderSnapshotJson
            },
            events = events.Select(item => new
            {
                item.Timestamp,
                EventType = item.EventType.ToString(),
                item.Path,
                item.OldPath
            })
        };

        File.WriteAllText(filePath, JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8);
    }

    public void ExportCsv(string filePath, IReadOnlyList<FileEventRecord> events)
    {
        var builder = new StringBuilder();
        builder.AppendLine("timestamp,event_type,path,old_path");
        foreach (var item in events)
        {
            builder
                .Append(Escape(item.Timestamp.ToString("O")))
                .Append(',')
                .Append(Escape(item.EventType.ToString()))
                .Append(',')
                .Append(Escape(item.Path))
                .Append(',')
                .Append(Escape(item.OldPath ?? string.Empty))
                .AppendLine();
        }

        File.WriteAllText(filePath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
