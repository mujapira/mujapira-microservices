using Contracts.Common;
using Contracts.Logs;
using Contracts.BugReport;
using Humanizer;
using BugReportService.Data;
using BugReportService.Models;
using Microsoft.EntityFrameworkCore;
using BugReportService.Helpers;
using LogLevel = Contracts.Logs.LogLevel;

namespace BugReportService.Services;

public class BugReportService(CorpContext ctx, IKafkaProducer producer, IConfiguration config) : IBugReportService
{
    private readonly CorpContext _ctx = ctx;
    private readonly IKafkaProducer _producer = producer;
    private readonly IConfiguration _config = config;

    public async Task<BugReportDto> CreateAsync(CreateBugReportDto dto, Guid? reporterUserId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var entity = new BugReport
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            LastActivityAt = now,
            ReporterUserId = reporterUserId,
            ReporterEmail = dto.ReporterEmail?.Trim(),
            PageUrl = dto.PageUrl?.Trim(),
            Severity = dto.Severity.ToModel(),
            Status = Models.BugStatus.open,
            Description = dto.Description,
            Steps = dto.Steps,
            Metadata = BugReportMapper.ToJsonDocument(dto.Metadata),
            ScreenshotCount = 0,
        };

        if (dto.Attachments is { Count: > 0 })
        {
            entity.Attachments = new List<BugAttachment>(dto.Attachments.Count);
            foreach (var a in dto.Attachments)
            {
                entity.Attachments.Add(new BugAttachment
                {
                    Id = Guid.NewGuid(),
                    BugReportId = entity.Id,
                    Url = a.Url,
                    ContentType = a.ContentType,
                    Bytes = a.Bytes,
                    Width = a.Width,
                    Height = a.Height,
                    Sha256 = string.IsNullOrWhiteSpace(a.Sha256Base64) ? null : Convert.FromBase64String(a.Sha256Base64),
                    CreatedAt = now
                });
            }
            entity.ScreenshotCount = entity.Attachments.Count;
        }

        _ctx.BugReports.Add(entity);
        await _ctx.SaveChangesAsync(ct);

        var siteEmail = _config["Support:BugReportEmail"] ?? "mujapira@gmail.com";
        var subject = $"[BUG][{entity.Severity}] {(entity.PageUrl ?? entity.Id.ToString())}";
        var preview = entity.Description.Truncate(140);

        var mailEvent = new
        {
            To = siteEmail,
            Subject = subject,
            Text = BugEmailBuilder.BuildPlainText(entity),
            Html = BugEmailBuilder.BuildHtml(entity),
            Attachments = entity.Attachments.Select(a => new
            {
                a.Url,
                a.ContentType,
                FileName = BugEmailBuilder.SafeFileNameFromUrl(a.Url)
            }).ToList()
        };
        _producer.ProduceFireAndForget(MailKafkaTopics.BugReported.GetTopicName(), mailEvent);

        var logDto = new LogMessageDto(
            Source: RegisteredMicroservices.BugReportService,
            Level: LogLevel.Info,
            Message: "Bug report criado",
            Timestamp: DateTime.UtcNow,
            Metadata: new Dictionary<string, object>
            {
                ["BugReportId"] = entity.Id,
                ["Severity"] = entity.Severity.ToString(),
                ["ReporterEmail"] = entity.ReporterEmail,
                ["PageUrl"] = entity.PageUrl,
                ["Preview"] = preview,
                ["Screenshots"] = entity.ScreenshotCount,
            }

        );
        _producer.ProduceFireAndForget(LogKafkaTopics.Logs.GetTopicName(), logDto);

        var created = await _ctx.BugReports.AsNoTracking()
            .Include(x => x.Attachments)
            .FirstAsync(x => x.Id == entity.Id, ct);

        return BugReportMapper.ToDto(created);
    }

    public async Task<BugReportDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var e = await _ctx.BugReports.AsNoTracking()
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return e is null ? null : BugReportMapper.ToDto(e);
    }

    public async Task<(IReadOnlyList<BugReportDto> Items, int Total)> SearchAsync(BugReportSearchQuery query, CancellationToken ct = default)
    {
        var q = _ctx.BugReports.AsNoTracking();

        if (query.Status.HasValue) q = q.Where(x => x.Status == query.Status.Value.ToModel());
        if (query.Severity.HasValue) q = q.Where(x => x.Severity == query.Severity.Value.ToModel());
        if (!string.IsNullOrWhiteSpace(query.Email)) q = q.Where(x => x.ReporterEmail == query.Email);
        if (query.From.HasValue) q = q.Where(x => x.CreatedAt >= query.From.Value);
        if (query.To.HasValue) q = q.Where(x => x.CreatedAt <= query.To.Value);

        var total = await q.CountAsync(ct);

        var items = await q.OrderByDescending(x => x.CreatedAt)
            .Skip(Math.Max(0, query.Skip))
            .Take(Math.Clamp(query.Limit, 1, 500))
            .Include(x => x.Attachments)
            .ToListAsync(ct);

        return (items.Select(BugReportMapper.ToDto).ToList(), total);
    }

    public async Task<bool> UpdateStatusAsync(Guid id, Contracts.BugReport.BugStatus status, Guid? assigneeUserId, CancellationToken ct = default)
    {
        var e = await _ctx.BugReports.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return false;

        e.Status = status.ToModel();
        e.AssigneeUserId = assigneeUserId;
        e.LastActivityAt = DateTimeOffset.UtcNow;
        await _ctx.SaveChangesAsync(ct);

        var logDto = new LogMessageDto(
            Source: RegisteredMicroservices.BugReportService,
            Level: LogLevel.Info,
            Message: "Bug report atualizado",
            Timestamp: DateTime.UtcNow,
            Metadata: new Dictionary<string, object>
            {
                ["BugReportId"] = e.Id,
                ["Status"] = e.Status.ToString(),
                ["Assignee"] = e.AssigneeUserId?.ToString()
            }
        );
        _producer.ProduceFireAndForget(LogKafkaTopics.Logs.GetTopicName(), logDto);

        return true;
    }
}