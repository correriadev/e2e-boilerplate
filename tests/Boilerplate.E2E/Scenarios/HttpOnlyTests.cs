using Allure.Net.Commons;
using Allure.Xunit.Attributes;
using Boilerplate.E2E.Fixtures;
using Xunit;

namespace Boilerplate.E2E.Scenarios;

[AllureEpic("Plataforma"), AllureFeature("Prontidão")]
public sealed class HttpOnlyTests(E2eFixture fixture) : IClassFixture<E2eFixture>
{
    [Fact, Trait("Category", "Http")]
    [AllureStory("Consultar saúde sem configurar clientes SQL Server ou Redis")]
    public async Task Api_is_ready()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await AllureApi.Step("Consultar prontidão por HTTP", async () =>
        {
            var response = await fixture.Api.SendAsync(HttpMethod.Get, "/health/ready", Guid.NewGuid(), cancellation: deadline.Token);
            Assert.Equal(200, response.Status);
        });
    }
}
