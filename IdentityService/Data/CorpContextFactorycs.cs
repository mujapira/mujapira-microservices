using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IdentityService.Data
{
    public sealed class CorpContextFactory : IDesignTimeDbContextFactory<CorpContext>
    {
        public CorpContext CreateDbContext(string[] args)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddEnvironmentVariables()
                .Build();

            var conn =
                config.GetConnectionString("Identity") ??
                config["IDENTITY_DB_CONNECTION"] ??
                "Host=localhost;Port=5432;Database=identity;Username=postgres;Password=postgres";

            var options = new DbContextOptionsBuilder<CorpContext>()
                .UseNpgsql(conn)
                .EnableSensitiveDataLogging(false)
                .Options;

            return new CorpContext(options);
        }
    }
}
