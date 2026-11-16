using System.Net.Http.Json;
using System.Text.Json;
using Allure.Net.Commons;
using Allure.Xunit.Attributes;
using Boilerplate.E2E.Fixtures;
using Boilerplate.E2E.Infrastructure.Redis;
using Boilerplate.E2E.Infrastructure.SqlServer;
using Boilerplate.E2E.Reporting;
using Xunit;

namespace Boilerplate.E2E.Scenarios;

[AllureEpic("Pedidos")]
[AllureFeature("Integração com catálogo e consulta")]
[AllureTag("http", "sqlserver", "redis")]
public sealed class ReferenceJourneyTests(E2eFixture fixture) : IClassFixture<E2eFixture>
{
    [Fact, Trait("Category", "Journey")]
    [AllureStory("Persistir preço externo e popular cache na consulta pública")]
    [AllureSeverity(SeverityLevel.critical)]
    public Task Create_query_and_observe_order() => Isolated(async (id, sql, redis, ct) =>
    {
        Guid returnedId = Guid.Empty;
        await AllureApi.Step("Criar pedido pela API", async () =>
        {
            var response = await fixture.Api.SendAsync(HttpMethod.Post, "/orders", id, new { reference = id, sku = "SKU-E2E" }, ct);
            Assert.Equal(201, response.Status);
            var order = ReadOrder(response.Body);
            Assert.Equal(id, order.Id);
            Assert.Equal(42.50m, order.Price);
            Assert.Equal("BRL", order.Currency);
            returnedId = order.Id;
        });
        await AllureApi.Step("Conferir pedido persistido no SQL Server", async () =>
        {
            var row = await sql.FindOrderAsync(returnedId, ct);
            Assert.NotNull(row);
            Assert.Equal(id, row.Reference);
            Assert.Equal("SKU-E2E", SafeSku(row.Sku));
            Assert.Equal(42.50m, row.Price);
            Assert.Equal("BRL", SafeCurrency(row.Currency));
        });
        await AllureApi.Step("Conferir cache vazio antes da consulta pública", async () =>
            Assert.False((await redis.ObserveOrderAsync(returnedId, ct)).Present));
        OrderContract? publicOrder = null;
        await AllureApi.Step("Consultar pedido pela API", async () =>
        {
            var response = await fixture.Api.SendAsync(HttpMethod.Get, $"/orders/{returnedId:D}", id, cancellation: ct);
            Assert.Equal(200, response.Status);
            publicOrder = ReadOrder(response.Body);
            Assert.Equal(id, publicOrder.Id);
            Assert.Equal(id, publicOrder.Reference);
            Assert.Equal("SKU-E2E", publicOrder.Sku);
            Assert.Equal(42.50m, publicOrder.Price);
            Assert.Equal("BRL", publicOrder.Currency);
        });
        await AllureApi.Step("Conferir pedido em cache no Redis", async () =>
        {
            var cache = await redis.ObserveOrderAsync(returnedId, ct);
            Assert.True(cache.Present);
            var cached = ReadOrder(cache.Value!);
            Assert.Equal(publicOrder, cached);
            Assert.True(cache.Ttl is { TotalSeconds: > 0 and <= 300 }, "TTL deve estar entre zero e 300 segundos.");
        });
        await VerifyCatalogCall(id, "SKU-E2E", ct);
    });

    [Fact, Trait("Category", "Journey")]
    [AllureStory("Rejeitar catálogo indisponível sem persistência ou cache")]
    [AllureSeverity(SeverityLevel.critical)]
    public Task External_failure_has_no_side_effects() => Isolated(async (id, sql, redis, ct) =>
    {
        await AllureApi.Step("Criar pedido com catálogo indisponível pela API", async () =>
        {
            var response = await fixture.Api.SendAsync(HttpMethod.Post, "/orders", id, new { reference = id, sku = "SKU-UNAVAILABLE" }, ct);
            Assert.Equal(502, response.Status);
            using var json = ParseSafe(response.Body);
            Assert.True(json.RootElement.TryGetProperty("code", out var code) && code.GetString() == "catalog_unavailable",
                "Contrato esperado: code=catalog_unavailable.");
        });
        await AllureApi.Step("Conferir ausência de pedido no SQL Server", async () =>
            Assert.Null(await sql.FindOrderAsync(id, ct)));
        await AllureApi.Step("Consultar tentativa rejeitada pela API", async () =>
            Assert.Equal(404, (await fixture.Api.SendAsync(HttpMethod.Get, $"/orders/{id:D}", id, cancellation: ct)).Status));
        await AllureApi.Step("Conferir ausência de pedido no Redis", async () =>
            Assert.False((await redis.ObserveOrderAsync(id, ct)).Present));
        await VerifyCatalogCall(id, "SKU-UNAVAILABLE", ct);
    });

    private async Task Isolated(Func<Guid, SqlServerClient, RedisClient, CancellationToken, Task> journey)
    {
        var sql = fixture.Sql();
        var redis = await fixture.RedisAsync();
        var id = Guid.NewGuid();
        var failed = false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await journey(id, sql, redis, deadline.Token); }
        catch { failed = true; throw; }
        finally
        {
            // Independent cleanup deadline, even if the journey expired. Attempt both resources.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var errors = 0;
            await AllureApi.Step("Limpar somente recursos desta jornada", async () =>
            {
                try { await sql.DeleteOrderAsync(id, cleanup.Token); } catch { errors++; }
                try { await redis.DeleteOrderAsync(id, cleanup.Token); } catch { errors++; }
                AllureEvidence.Json("Resultado da limpeza", new { id, errors, originalFailurePreserved = failed });
            });
            if (errors > 0 && !failed) throw new InvalidOperationException("Limpeza incompleta dos recursos da jornada. Consulte o step de limpeza.");
        }
    }

    private async Task VerifyCatalogCall(Guid correlation, string sku, CancellationToken ct)
    {
        // Optional journal observation is independent of the three mandatory test endpoints.
        var url = Environment.GetEnvironmentVariable("E2E_WIREMOCK_ADMIN_URL");
        if (string.IsNullOrWhiteSpace(url)) return;
        await AllureApi.Step("Conferir chamada externa correlacionada no WireMock", async () =>
        {
            var count = await AllureEvidence.Operation("WireMock journal", async () =>
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https") || endpoint.UserInfo.Length > 0 || endpoint.Query.Length > 0)
                    throw new InvalidOperationException();
                using var client = new HttpClient { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(5) };
                using var response = await client.GetAsync("__admin/requests", ct);
                response.EnsureSuccessStatusCode();
                using var journal = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                return journal.RootElement.EnumerateArray().Count(entry =>
                {
                    var request = entry.GetProperty("Request");
                    return request.GetProperty("Path").GetString() == $"/catalog/products/{sku}" &&
                        request.GetProperty("Headers").EnumerateObject().Any(header =>
                            header.Name.Equals("X-Correlation-Id", StringComparison.OrdinalIgnoreCase) &&
                            header.Value.EnumerateArray().Any(value => value.GetString() == correlation.ToString()));
                });
            });
            AllureEvidence.Json("Projeção do journal", new { method = "GET", route = $"/catalog/products/{sku}", correlation, count });
            Assert.Equal(1, count);
        });
    }

    private static JsonDocument ParseSafe(string body)
    {
        try { return JsonDocument.Parse(body); }
        catch { throw new InvalidOperationException("Contrato HTTP/cache inválido: JSON esperado. Conteúdo bruto omitido."); }
    }

    private static OrderContract ReadOrder(string body)
    {
        try
        {
            using var json = ParseSafe(body);
            var root = json.RootElement;
            return new(root.GetProperty("id").GetGuid(), root.GetProperty("reference").GetGuid(),
                SafeSku(root.GetProperty("sku").GetString()), root.GetProperty("price").GetDecimal(),
                SafeCurrency(root.GetProperty("currency").GetString()));
        }
        catch { throw new InvalidOperationException("Contrato de pedido inválido. Confira a projeção sanitizada do step."); }
    }
    private static string SafeSku(string? value) => value is "SKU-E2E" or "SKU-UNAVAILABLE" ? value : "[unexpected sku]";
    private static string SafeCurrency(string? value) => value is "BRL" or "USD" or "EUR" ? value : "[unexpected currency]";
    private sealed record OrderContract(Guid Id, Guid Reference, string Sku, decimal Price, string Currency);
}
