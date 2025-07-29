using Microsoft.EntityFrameworkCore;

namespace AuthService.Services
{
    public class MigrationService(IServiceProvider sp, ILogger<MigrationService> logger) : IHostedService
    {
        private readonly IServiceProvider _sp = sp;
        private readonly ILogger<MigrationService> _logger = logger;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Applying pending migrations to AuthDb...");

            try
            {
                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

                await db.Database.MigrateAsync(cancellationToken);

                _logger.LogInformation("AuthDb migrations applied successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply AuthDb migrations.");
                throw;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}