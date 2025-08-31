using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Text.Json;
using BugReportService.Models;

namespace BugReportService.Data;

public class CorpContext : DbContext
{
    public DbSet<BugReport> BugReports => Set<BugReport>();
    public DbSet<BugAttachment> BugAttachments => Set<BugAttachment>();
    public DbSet<BugComment> BugComments => Set<BugComment>();

    public CorpContext(DbContextOptions<CorpContext> opts) : base(opts) { }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresEnum<BugSeverity>();
        b.HasPostgresEnum<BugStatus>();
        b.Entity<BugReport>(e =>
        {
            e.Property(p => p.Metadata).HasColumnType("jsonb");
            e.HasMany(p => p.Attachments).WithOne(a => a.BugReport).HasForeignKey(a => a.BugReportId);
            e.HasMany(p => p.Comments).WithOne(a => a.BugReport).HasForeignKey(a => a.BugReportId);
            e.HasIndex(p => p.CreatedAt);
            e.HasIndex(p => p.Status);
            e.HasIndex(p => p.Severity);
            e.HasIndex(p => p.ReporterEmail);
        });
    }
}
