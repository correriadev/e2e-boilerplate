using Boilerplate.E2E.Configuration;
using Boilerplate.E2E.Infrastructure.Http;
using Boilerplate.E2E.Infrastructure.Redis;
using Boilerplate.E2E.Infrastructure.SqlServer;
using Boilerplate.E2E.Reporting;
using StackExchange.Redis;

namespace Boilerplate.E2E.Fixtures;

public sealed class E2eFixture : Xunit.IAsyncLifetime
{
    public E2eSettings Settings { get; } = E2eSettings.Load();
    private readonly HttpClient http;
    private readonly Lazy<Task<ConnectionMultiplexer>> redis;
    public ApiClient Api { get; }

    public E2eFixture()
    {
        http = new HttpClient { BaseAddress = Settings.BaseUrl, Timeout = TimeSpan.FromSeconds(10) };
        Api = new ApiClient(http);
        redis = new(() => ConnectionMultiplexer.ConnectAsync(Settings.RedisOptions()));
    }

    public SqlServerClient Sql()
    {
        Settings.RequireSqlServer();
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(Settings.SqlServerConnectionString) { ConnectTimeout = 5 };
        return new(builder.ConnectionString);
    }

    public async Task<RedisClient> RedisAsync()
    {
        _ = Settings.RedisOptions();
        return new(await AllureEvidence.Operation("Redis", () => redis.Value));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        http.Dispose();
        if (redis.IsValueCreated && redis.Value.IsCompletedSuccessfully)
            await redis.Value.Result.DisposeAsync();
    }
}
