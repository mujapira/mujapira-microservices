namespace Common.Observability;

public static class Masking
{
    /// <summary>
    /// Retorna um valor seguro, substituindo nulo/vazio por "(n/a)".
    /// </summary>
    public static string Safe(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(n/a)" : value!;

    /// <summary>
    /// Mascara mantendo X primeiros e Y últimos caracteres.
    /// Ex.: abcdefg => a***g (keepFirst=1, keepLast=1)
    /// </summary>
    public static string MaskKeepFirstLast(string? s, int keepFirst = 1, int keepLast = 1)
    {
        if (string.IsNullOrEmpty(s)) return "(n/a)";
        if (s.Length <= keepFirst + keepLast) return new string('*', s.Length);
        return $"{s[..keepFirst]}***{s[^keepLast..]}";
    }

    /// <summary>
    /// Mascara credenciais em Kafka bootstrap servers.
    /// Ex.: user:pass@host:9092 => ***@host:9092
    /// </summary>
    public static string MaskBootstrapServers(string? bootstrap)
    {
        if (string.IsNullOrWhiteSpace(bootstrap)) return "(n/a)";
        var parts = bootstrap.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            var at = p.IndexOf('@');
            parts[i] = at > 0 ? $"***@{p[(at + 1)..]}" : p;
        }
        return string.Join(",", parts);
    }

    /// <summary>
    /// Mascara connection string do Postgres.
    /// </summary>
    public static string MaskPostgresConnectionString(string conn)
    {
        try
        {
            var b = new Npgsql.NpgsqlConnectionStringBuilder(conn);
            string host = string.IsNullOrWhiteSpace(b.Host) ? "(n/a)" : b.Host;
            string db = string.IsNullOrWhiteSpace(b.Database) ? "(n/a)" : b.Database;
            string user = string.IsNullOrWhiteSpace(b.Username) ? "(n/a)" : MaskKeepFirstLast(b.Username);
            return $"Host={host}; Database={db}; Username={user}; Password=***";
        }
        catch
        {
            return $"ConnectionString(len={conn?.Length ?? 0})";
        }
    }

    /// <summary>
    /// Mascara connection string do Mongo.
    /// </summary>
    public static string MaskMongoConnectionString(string conn)
    {
        try
        {
            var url = new MongoDB.Driver.MongoUrl(conn);
            var host = url.Server.Host ?? "(n/a)";
            var port = url.Server.Port;
            var db = string.IsNullOrWhiteSpace(url.DatabaseName) ? "(n/a)" : url.DatabaseName;
            var user = string.IsNullOrWhiteSpace(url.Username) ? "(n/a)" : MaskKeepFirstLast(url.Username);
            return $"mongodb://{user}@{host}:{port}/{db}?...";
        }
        catch
        {
            return $"MongoConnectionString(len={conn?.Length ?? 0})";
        }
    }

    public static string MaskRedisEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return "(n/a)";
        var at = endpoint.IndexOf('@');
        if (at > 0)
            return $"***@{endpoint[(at + 1)..]}";
        return endpoint;
    }
}
