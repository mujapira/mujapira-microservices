using System.Text.Json.Serialization;

namespace Contracts.BugReport
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BugSeverity { Low, Medium, High }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BugStatus { Open, Triaged, InProgress, Resolved, Closed }

    public record CreateBugAttachmentDto(
        string Url,
        string ContentType,
        int Bytes,
        int? Width = null,
        int? Height = null,
        string? Sha256Base64 = null
    );

    public record CreateBugReportDto(
        string Description,
        string? Steps = null,
        string? PageUrl = null,
        BugSeverity Severity = BugSeverity.Medium,
        string? ReporterEmail = null,
        Dictionary<string, object>? Metadata = null,
        IReadOnlyList<CreateBugAttachmentDto>? Attachments = null
    );

    public record BugAttachmentDto(
        Guid Id,
        string Url,
        string ContentType,
        int Bytes,
        int? Width,
        int? Height
    );

    public record BugReportDto(
        Guid Id,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastActivityAt,
        Guid? ReporterUserId,
        string? ReporterEmail,
        string? PageUrl,
        BugSeverity Severity,
        BugStatus Status,
        string Description,
        string? Steps,
        int ScreenshotCount,
        Dictionary<string, object>? Metadata,
        Guid? AssigneeUserId,
        IReadOnlyList<BugAttachmentDto> Attachments
    );

    public record BugReportSearchQuery(
        BugStatus? Status = null,
        BugSeverity? Severity = null,
        string? Email = null,
        DateTimeOffset? From = null,
        DateTimeOffset? To = null,
        int Skip = 0,
        int Limit = 50
    );
}