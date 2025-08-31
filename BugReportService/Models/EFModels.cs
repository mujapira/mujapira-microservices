using System.Text.Json;

namespace BugReportService.Models
{
    public enum BugSeverity { low, medium, high }
    public enum BugStatus { open, triaged, in_progress, resolved, closed }

    public class BugReport
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
        public Guid? ReporterUserId { get; set; }
        public string? ReporterEmail { get; set; }
        public string? PageUrl { get; set; }
        public BugSeverity Severity { get; set; } = BugSeverity.medium;
        public BugStatus Status { get; set; } = BugStatus.open;
        public string Description { get; set; } = null!;
        public string? Steps { get; set; }
        public JsonDocument? Metadata { get; set; }
        public int ScreenshotCount { get; set; }
        public Guid? AssigneeUserId { get; set; }
        public List<BugAttachment> Attachments { get; set; } = new();
        public List<BugComment> Comments { get; set; } = new();
    }

    public class BugAttachment
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid BugReportId { get; set; }
        public BugReport BugReport { get; set; } = null!;
        public string Url { get; set; } = null!;
        public string ContentType { get; set; } = null!;
        public int Bytes { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public byte[]? Sha256 { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public class BugComment
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid BugReportId { get; set; }
        public BugReport BugReport { get; set; } = null!;
        public Guid? AuthorUserId { get; set; }
        public string Body { get; set; } = null!;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
