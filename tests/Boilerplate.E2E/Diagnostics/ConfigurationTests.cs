using System.Text.Json;
using Allure.Net.Commons;
using Allure.Xunit.Attributes;
using Boilerplate.E2E.Configuration;
using Boilerplate.E2E.Infrastructure.Http;
using Boilerplate.E2E.Reporting;
using Xunit;

namespace Boilerplate.E2E.Diagnostics;

[AllureEpic("Plataforma"), AllureFeature("Configuração e evidências")]
public sealed class ConfigurationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "boilerplate-config-" + Guid.NewGuid().ToString("N"));

    public ConfigurationTests()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "appsettings.json"), """
            { "baseurl": "http://localhost:5080", "sqlserver-connectionstring": "", "redis-connectionstring": "" }
            """);
    }
    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact, Trait("Category", "Configuration")]
    [AllureStory("Aplicar JSON local antes dos overrides de ambiente")]
    public void Local_json_precedes_base_and_environment_precedes_local()
    {
        File.WriteAllText(Path.Combine(directory, "appsettings.local.json"), """{ "baseurl": "https://local.invalid/" }""");
        Assert.Equal("https://local.invalid/", E2eSettings.Load(directory, _ => null).BaseUrl.AbsoluteUri);
        Assert.Equal("https://environment.invalid/", E2eSettings.Load(directory, name => name == "E2E_BASEURL" ? "https://environment.invalid/" : null).BaseUrl.AbsoluteUri);
        AllureApi.Step("Validar arquivo local e override de ambiente", () =>
            AllureEvidence.Json("Precedência", new { order = "base JSON < local JSON < environment" }));
    }

    [Fact, Trait("Category", "Configuration")]
    [AllureStory("Localizar JSON no diretório de saída e priorizar ambiente")]
    public void Json_is_stable_and_environment_wins()
    {
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "appsettings.json")), "JSON deve ser copiado para o diretório de saída.");
        var json = E2eSettings.Load(directory, environment: _ => null);
        Assert.Equal("http://localhost:5080/", json.BaseUrl.AbsoluteUri);
        var overrides = E2eSettings.Load(directory, environment: name => name switch
        {
            "E2E_BASEURL" => "https://example.invalid/api/",
            "E2E_SQLSERVER_CONNECTIONSTRING" => "Server=localhost;Database=test;Integrated Security=True",
            "E2E_REDIS_CONNECTIONSTRING" => "localhost:6379",
            _ => null
        });
        Assert.Equal("https://example.invalid/api/", overrides.BaseUrl.AbsoluteUri);
        overrides.RequireSqlServer();
        _ = overrides.RedisOptions();
        AllureApi.Step("Validar precedência explícita", () =>
            AllureEvidence.Json("Configuração", new { jsonFoundInOutput = true, environmentWins = true, mappedKeys = 3 }));
    }

    [Fact, Trait("Category", "Configuration")]
    [AllureStory("Permitir HTTP sem exigir SQL Server ou Redis")]
    public void Missing_optional_dependencies_are_actionable_when_used()
    {
        var settings = E2eSettings.Load(directory, environment: _ => null);
        var sql = Assert.Throws<InvalidOperationException>(settings.RequireSqlServer);
        var redis = Assert.Throws<InvalidOperationException>(() => settings.RedisOptions());
        Assert.Contains("E2E_SQLSERVER_CONNECTIONSTRING", sql.Message);
        Assert.Contains("E2E_REDIS_CONNECTIONSTRING", redis.Message);
        AllureApi.Step("Validar configuração obrigatória por componente", () =>
            AllureEvidence.Json("Validação", new { httpOnlyAllowed = true, missingSqlMessageActionable = true, missingRedisMessageActionable = true }));
    }

    [Fact, Trait("Category", "Configuration")]
    [AllureStory("Rejeitar credenciais em URL e parse inválido sem divulgá-los")]
    public void Unsafe_configuration_is_rejected_without_echoing_values()
    {
        var secret = Environment.GetEnvironmentVariable("E2E_SYNTHETIC_SECRET") ?? "local-evidence-canary";
        var url = Assert.Throws<InvalidOperationException>(() => E2eSettings.Load(directory, environment: name =>
            name == "E2E_BASEURL" ? $"http://user:{secret}@localhost/" : null));
        var settings = new E2eSettings(new Uri("http://localhost"), $"Invalid={secret}", $"localhost,invalid={secret}");
        var sql = Assert.Throws<InvalidOperationException>(settings.RequireSqlServer);
        var redis = Assert.Throws<InvalidOperationException>(() => settings.RedisOptions());
        Assert.True(new[] { url.Message, sql.Message, redis.Message }.All(message => !message.Contains(secret)), "Mensagens devem omitir valores de configuração.");
        AllureApi.Step("Validar mensagens sanitizadas", () =>
            AllureEvidence.Json("Validação", new { credentialUrlRejected = true, malformedConnectionsRejected = true, secretsOmitted = true }));
    }

    [Fact, Trait("Category", "Configuration")]
    [AllureStory("Descartar headers, query e campos inesperados antes de anexar")]
    public async Task Synthetic_secret_crosses_http_capture_without_publication()
    {
        var secret = Environment.GetEnvironmentVariable("E2E_SYNTHETIC_SECRET") ?? "local-evidence-canary";
        using var handler = new EchoHandler(secret);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost"), Timeout = TimeSpan.FromSeconds(5) };
        var api = new ApiClient(http);
        await AllureApi.Step("Capturar HTTP adversarial com projeção permitida", async () =>
        {
            var response = await api.SendAsync(HttpMethod.Get, "/orders?token=" + Uri.EscapeDataString(secret), Guid.NewGuid());
            var safe = JsonSerializer.Serialize(EvidenceSanitizer.Project(response.Body));
            Assert.True(!safe.Contains(secret), "Projeção deve descartar valores fora do contrato.");
            Assert.Equal("/orders", EvidenceSanitizer.Route("/orders?token=" + secret));
        });
    }

    private sealed class EchoHandler(string secret) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { token = secret, password = secret,
                    sku = secret, currency = secret, id = secret, cookie = secret, price = 42.50m }))
            });
    }
}
