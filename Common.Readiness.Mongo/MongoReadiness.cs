using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Common.Readiness.Mongo;

public static class MongoReadiness
{
    public static async Task WaitAsync(
        string connectionString,
        ILogger logger,
        int maxRetries = 8,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null)
    {
        int attempt = 0;
        var delay = initialDelay ?? TimeSpan.FromSeconds(1);
        var maxCap = maxDelay ?? TimeSpan.FromSeconds(30);

        while (true)
        {
            try
            {
                var client = new MongoClient(connectionString);
                var db = client.GetDatabase("admin");
                await db.RunCommandAsync((Command<BsonDocument>)"{ ping: 1 }");
                logger.LogInformation("MongoDB disponível.");
                return;
            }
            catch (Exception ex)
            {
                attempt++;
                if (attempt >= maxRetries)
                {
                    logger.LogError(ex, "Falha ao conectar no MongoDB após {Attempt} tentativas.", attempt);
                    throw;
                }
                logger.LogWarning("MongoDB indisponível (tentativa {Attempt}). Retry em {Delay}s. Motivo: {Reason}",
                    attempt, delay.TotalSeconds, ex.Message);
                await Task.Delay(delay);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxCap.TotalSeconds));
            }
        }
    }

    public static async Task EnsureUserAsync(
        string rootConnectionString,
        string appDbName,
        string appUser,
        string appPassword,
        ILogger logger,
        int maxRetries = 5)
    {
        int attempt = 0;
        var delay = TimeSpan.FromSeconds(1);

        while (true)
        {
            try
            {
                var rootClient = new MongoClient(rootConnectionString);
                var appDb = rootClient.GetDatabase(appDbName);

                var usersInfo = await appDb.RunCommandAsync<BsonDocument>(new BsonDocument { { "usersInfo", appUser } });
                var userArray = usersInfo.GetValue("users").AsBsonArray;

                if (userArray.Count == 0)
                {
                    logger.LogInformation("Usuário Mongo '{User}' não existe em '{Db}', criando com role readWrite.", appUser, appDbName);
                    var createUserCmd = new BsonDocument
                    {
                        { "createUser", appUser },
                        { "pwd", appPassword },
                        { "roles", new BsonArray { new BsonDocument { { "role", "readWrite" }, { "db", appDbName } } } }
                    };
                    await appDb.RunCommandAsync<BsonDocument>(createUserCmd);
                    logger.LogInformation("Usuário Mongo criado com sucesso.");
                }
                else
                {
                    logger.LogInformation("Usuário Mongo '{User}' já existe em '{Db}'.", appUser, appDbName);
                }

                return;
            }
            catch (Exception ex)
            {
                attempt++;
                if (attempt >= maxRetries)
                {
                    logger.LogError(ex, "Falha garantindo usuário Mongo após {Attempt} tentativas.", attempt);
                    throw;
                }
                logger.LogWarning("Erro garantindo usuário Mongo (tentativa {Attempt}). Retry em {Delay}s. Motivo: {Reason}",
                    attempt, delay.TotalSeconds, ex.Message);
                await Task.Delay(delay);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }
}
