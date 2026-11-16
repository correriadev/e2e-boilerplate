using System.Data;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient("catalog", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["CatalogBaseUrl"]!);
    client.Timeout = TimeSpan.FromSeconds(5);
});
// Do not log requests, headers, bodies or connection exceptions in this sample.
builder.Logging.ClearProviders();
var app = builder.Build();
var sql = builder.Configuration.GetConnectionString("SqlServer")!;
var redisOptions = ConfigurationOptions.Parse(builder.Configuration.GetConnectionString("Redis")!);
redisOptions.ConnectTimeout = redisOptions.AsyncTimeout = 5000;
redisOptions.AbortOnConnectFail = false;
redisOptions.IncludeDetailInExceptions = false;
using var redis = await ConnectionMultiplexer.ConnectAsync(redisOptions);
using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(120));
var initialized = false;
while (!startup.IsCancellationRequested)
{
    try
    {
        var master = new SqlConnectionStringBuilder(sql) { InitialCatalog = "master", ConnectTimeout = 3 };
        await using (var connection = new SqlConnection(master.ConnectionString))
        {
            await connection.OpenAsync(startup.Token);
            await using var create = new SqlCommand("IF DB_ID(N'BoilerplateE2E') IS NULL CREATE DATABASE BoilerplateE2E", connection) { CommandTimeout = 10 };
            await create.ExecuteNonQueryAsync(startup.Token);
        }
        await using (var connection = new SqlConnection(sql))
        {
            await connection.OpenAsync(startup.Token);
            await using var schema = new SqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "init.sql"), startup.Token), connection) { CommandTimeout = 10 };
            await schema.ExecuteNonQueryAsync(startup.Token);
        }
        await redis.GetDatabase().PingAsync().WaitAsync(startup.Token);
        var clients = app.Services.GetRequiredService<IHttpClientFactory>();
        using var catalog = clients.CreateClient("catalog");
        var product = await catalog.GetFromJsonAsync<Product>("catalog/products/SKU-E2E", startup.Token);
        using var unavailable = await catalog.GetAsync("catalog/products/SKU-UNAVAILABLE", startup.Token);
        if (product is not { Price: 42.50m, Currency: "BRL" } || (int)unavailable.StatusCode != 503)
            throw new InvalidOperationException();
        initialized = true;
        break;
    }
    catch { try { await Task.Delay(1000, startup.Token); } catch (OperationCanceledException) { break; } }
}
if (!initialized) throw new InvalidOperationException("Stack não ficou pronta em 120 segundos. Confira schema, Redis e mappings do catálogo.");

app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (Exception) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new { code = "dependency_unavailable" });
    }
});

app.MapGet("/health/ready", async (IHttpClientFactory clients, CancellationToken ct) =>
{
    await using var connection = new SqlConnection(sql);
    await connection.OpenAsync(ct);
    await using var command = new SqlCommand("SELECT TOP(0) Id FROM dbo.Orders", connection) { CommandTimeout = 5 };
    await command.ExecuteScalarAsync(ct);
    await redis.GetDatabase().PingAsync().WaitAsync(ct);
    using var catalog = clients.CreateClient("catalog");
    var product = await catalog.GetFromJsonAsync<Product>("catalog/products/SKU-E2E", ct);
    return product is { Price: 42.50m, Currency: "BRL" } ? Results.Ok(new { code = "ready" }) : Results.StatusCode(503);
});

app.MapPost("/orders", async (CreateOrder input, HttpContext context, IHttpClientFactory clients, CancellationToken ct) =>
{
    if (input.Reference == Guid.Empty || input.Sku is not ("SKU-E2E" or "SKU-UNAVAILABLE")) return Results.BadRequest();
    using var catalog = clients.CreateClient("catalog");
    using var request = new HttpRequestMessage(HttpMethod.Get, $"catalog/products/{input.Sku}");
    var correlation = Guid.TryParse(context.Request.Headers["X-Correlation-Id"], out var parsed) ? parsed : input.Reference;
    request.Headers.Add("X-Correlation-Id", correlation.ToString());
    using var response = await catalog.SendAsync(request, ct);
    if (!response.IsSuccessStatusCode) return Results.Json(new { code = "catalog_unavailable" }, statusCode: 502);
    var product = await response.Content.ReadFromJsonAsync<Product>(ct);
    if (product is null || product.Price < 0 || product.Currency != "BRL") return Results.StatusCode(502);
    var order = new Order(input.Reference, input.Reference, input.Sku, product.Price, product.Currency);
    await using var connection = new SqlConnection(sql);
    await connection.OpenAsync(ct);
    await using var command = new SqlCommand("INSERT INTO dbo.Orders(Id, Reference, Sku, Price, Currency) VALUES(@id,@reference,@sku,@price,@currency)", connection) { CommandTimeout = 5 };
    command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = order.Id;
    command.Parameters.Add("@reference", SqlDbType.UniqueIdentifier).Value = order.Reference;
    command.Parameters.Add("@sku", SqlDbType.NVarChar, 64).Value = order.Sku;
    var price = command.Parameters.Add("@price", SqlDbType.Decimal); price.Precision = 18; price.Scale = 2; price.Value = order.Price;
    command.Parameters.Add("@currency", SqlDbType.Char, 3).Value = order.Currency;
    await command.ExecuteNonQueryAsync(ct);
    return Results.Created($"/orders/{order.Id:D}", order);
});

app.MapGet("/orders/{id:guid}", async (Guid id, CancellationToken ct) =>
{
    var key = $"e2e:orders:{id:D}";
    var cache = await redis.GetDatabase().StringGetAsync(key).WaitAsync(ct);
    if (cache.HasValue) return Results.Content(cache.ToString(), "application/json");
    await using var connection = new SqlConnection(sql);
    await connection.OpenAsync(ct);
    await using var command = new SqlCommand("SELECT Id, Reference, Sku, Price, Currency FROM dbo.Orders WHERE Id=@id", connection) { CommandTimeout = 5 };
    command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
    await using var reader = await command.ExecuteReaderAsync(ct);
    if (!await reader.ReadAsync(ct)) return Results.Json(new { code = "not_found" }, statusCode: 404);
    var order = new Order(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetDecimal(3), reader.GetString(4));
    var json = JsonSerializer.Serialize(order, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    await redis.GetDatabase().StringSetAsync(key, json, TimeSpan.FromMinutes(5)).WaitAsync(ct);
    return Results.Content(json, "application/json");
});

await app.RunAsync();
record Product(decimal Price, string Currency);
record CreateOrder(Guid Reference, string Sku);
record Order(Guid Id, Guid Reference, string Sku, decimal Price, string Currency);
