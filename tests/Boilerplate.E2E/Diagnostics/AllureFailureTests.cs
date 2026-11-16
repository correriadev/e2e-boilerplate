using Allure.Net.Commons;
using Allure.Xunit.Attributes;
using Boilerplate.E2E.Fixtures;
using Xunit;

namespace Boilerplate.E2E.Diagnostics;

public sealed class DiagnosticFactAttribute : FactAttribute
{
    public DiagnosticFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("E2E_DIAGNOSTIC") != "1")
            Skip = "Opt-in: use scripts/e2e.ps1 -Mode Diagnostic.";
    }
}

[AllureEpic("Plataforma"), AllureFeature("Relatórios")]
public sealed class AllureFailureTests(E2eFixture fixture) : IClassFixture<E2eFixture>
{
    [DiagnosticFact, Trait("Category", "Diagnostic")]
    [AllureStory("Localizar asserção intencional após evidência HTTP")]
    public async Task Intentional_assertion_after_http_evidence()
    {
        await AllureApi.Step("Diagnóstico: falhar asserção após capturar HTTP", async () =>
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var response = await fixture.Api.SendAsync(HttpMethod.Get, "/health/ready", Guid.NewGuid(), cancellation: deadline.Token);
            Assert.Equal(200, response.Status);
            Assert.Fail("E2E_DIAGNOSTIC_EXPECTED: asserção intencional após evidência HTTP.");
        });
    }
}
