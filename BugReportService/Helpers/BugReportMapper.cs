using System.Text.Json;
using Contracts.BugReport;
using BugReportService.Models;

namespace BugReportService.Helpers;

public static class BugReportMapper
{
    public static BugReportDto ToDto(BugReport e)
    {
        return new BugReportDto(
            Id: e.Id,
            CreatedAt: e.CreatedAt,
            LastActivityAt: e.LastActivityAt,
            ReporterUserId: e.ReporterUserId,
            ReporterEmail: e.ReporterEmail,
            PageUrl: e.PageUrl,
            Severity: e.Severity.ToContract(),
            Status: e.Status.ToContract(),
            Description: e.Description,
            Steps: e.Steps,
            ScreenshotCount: e.ScreenshotCount,
            Metadata: e.Metadata is null ? null
                : JsonSerializer.Deserialize<Dictionary<string, object?>>(e.Metadata.RootElement.GetRawText()),
            AssigneeUserId: e.AssigneeUserId,
            Attachments: (e.Attachments ?? new List<BugAttachment>())
                .Select(ToDto)
                .ToList()
        );
    }

    public static BugAttachmentDto ToDto(BugAttachment a)
        => new(a.Id, a.Url, a.ContentType, a.Bytes, a.Width, a.Height);

    public static JsonDocument? ToJsonDocument(Dictionary<string, object?>? dict)
        => dict is null ? null : JsonDocument.Parse(JsonSerializer.Serialize(dict));
}
