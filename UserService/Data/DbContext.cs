using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;
using UserService.Models;

namespace UserService.Data;

public class CorpContext : DbContext
{
    public DbSet<User> Users { get; set; } = null!;

    public CorpContext(DbContextOptions<CorpContext> opts)
       : base(opts)
    { }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(u => u.Id);
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
        });
    }
}
