using StackExchange.Redis;
using Boilerplate.E2E.Reporting;

namespace Boilerplate.E2E.Infrastructure.Redis;

public sealed record CacheObservation(bool Present, string? Value, TimeSpan? Ttl);

public sealed class RedisClient(IConnectionMultiplexer connection)
{
    public static string Key(Guid id) => $"e2e:orders:{id:D}";

    public Task<CacheObservation> ObserveOrderAsync(Guid id, CancellationToken cancellation) =>
        AllureEvidence.Operation("Redis", async () =>
        {
            var database = connection.GetDatabase();
            var value = await database.StringGetAsync(Key(id)).WaitAsync(cancellation);
            var ttl = await database.KeyTimeToLiveAsync(Key(id)).WaitAsync(cancellation);
            AllureEvidence.Json("Pedido em cache", new { operation = "GET + TTL", key = Key(id), present = value.HasValue,
                observed = EvidenceSanitizer.Project(value.HasValue ? value.ToString() : null), ttlSeconds = ttl?.TotalSeconds });
            return new CacheObservation(value.HasValue, value.HasValue ? value.ToString() : null, ttl);
        });

    public Task<bool> DeleteOrderAsync(Guid id, CancellationToken cancellation) =>
        AllureEvidence.Operation("Redis", async () =>
        {
            var deleted = await connection.GetDatabase().KeyDeleteAsync(Key(id)).WaitAsync(cancellation);
            AllureEvidence.Json("Limpeza Redis", new { operation = "DEL owned key", key = Key(id), deleted });
            return deleted;
        });
}
