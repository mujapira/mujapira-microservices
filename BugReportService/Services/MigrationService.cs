using BugReportService.Data;
using Microsoft.EntityFrameworkCore;

namespace BugReportService.Services
{
    public class MigrationService : IHostedService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<MigrationService> _logger;

        public MigrationService(IServiceProvider sp, ILogger<MigrationService> logger)
        {
            _sp = sp;
            _logger = logger;
        }

        //cd BugReportService
        //dotnet ef migrations add InitialIdentity --startup-project BugReportService.csproj

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Aplicando migrações pendentes ao banco de dados...");

            try
            {
                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CorpContext>();

                await db.Database.MigrateAsync(cancellationToken);

                _logger.LogInformation("Migrações aplicadas com sucesso.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao aplicar migrações.");
                throw;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
