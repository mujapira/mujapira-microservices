using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;
using IdentityService.Models;

namespace IdentityService.Data;

public class CorpContext : DbContext
{
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<AuthRefreshToken> RefreshTokens { get; set; }

    public CorpContext(DbContextOptions<CorpContext> opts)
       : base(opts)
    { }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email)
             .IsRequired()
             .HasMaxLength(200);
            e.Property(u => u.Password)
             .IsRequired();
            e.Property(u => u.IsAdmin)
             .IsRequired();
            e.Property(u => u.CreatedAt)
             .HasColumnType("timestamptz")
             .IsRequired();
            e.Property(u => u.Name).IsRequired();
        });

        mb.Entity<AuthRefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasKey(t => t.Id);
            e.Property(t => t.Token)
             .IsRequired();
            e.Property(t => t.JwtId)
             .IsRequired();
            e.Property(t => t.CreationDate)
             .HasColumnType("timestamptz")
             .IsRequired();
            e.Property(t => t.ExpiryDate)
             .HasColumnType("timestamptz")
             .IsRequired();
            e.Property(t => t.Used)
             .IsRequired();
            e.Property(t => t.Invalidated)
             .IsRequired();
            e.Property(t => t.UserId)
             .IsRequired();
            e.HasOne<User>()
             .WithMany()
             .HasForeignKey(t => t.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
