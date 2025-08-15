using StackExchange.Redis;

namespace IdentityService.Redis
{
    public class RedisRateLimiter : IRateLimiter
    {
        private readonly IConnectionMultiplexer _redis;

        public RedisRateLimiter(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

        public async Task<(bool allowed, int retryAfterSeconds)> TryAcquireAsync(string key, int limit, TimeSpan window)
        {
            var db = _redis.GetDatabase();
            // chave: e.g. "rl:login:email:foo@example.com"
            var count = await db.StringIncrementAsync(key);
            if (count == 1)
            {
                // primeira vez, definir expiration
                await db.KeyExpireAsync(key, window);
            }

            if (count > limit)
            {
                // pega TTL restante para informar Retry-After
                var ttl = await db.KeyTimeToLiveAsync(key);
                var retry = ttl.HasValue ? (int)ttl.Value.TotalSeconds : (int)window.TotalSeconds;
                return (false, retry);
            }

            return (true, 0);
        }
    }
}
