using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using UserService.Data;

namespace UserService.Services
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
