namespace AuthService.Redis
{

    public interface IRateLimiter
    {
        /// <summary>
        /// Tenta consumir uma unidade do bucket. Retorna false se excedeu o limite.
        /// </summary>
        /// <param name="key">Chave única (ex: por email/ip/token)</param>
        /// <param name="limit">Quantidade máxima no intervalo</param>
        /// <param name="window">Janela do limitador (ex: 1 minuto)</param>
        /// <returns>Tuple: (allowed?, segundos até reset)</returns>
        Task<(bool allowed, int retryAfterSeconds)> TryAcquireAsync(string key, int limit, TimeSpan window);
    }
}