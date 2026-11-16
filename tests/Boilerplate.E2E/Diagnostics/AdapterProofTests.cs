using System.Text;
using Allure.Net.Commons;
using Allure.Xunit.Attributes;
using Xunit;

namespace Boilerplate.E2E.Diagnostics;

[AllureEpic("Plataforma")]
[AllureFeature("Relatórios")]
public class AdapterProofTests
{
    [Fact, Trait("Category", "Proof")]
    [AllureStory("Descobrir teste, aguardar step e anexar JSON")]
    public async Task Async_step_has_attachment()
    {
        await AllureApi.Step("Validar step assíncrono e anexo", async () =>
        {
            await Task.Yield();
            AllureApi.AddAttachment("Prova do adaptador", "application/json",
                Encoding.UTF8.GetBytes("{\"adapter\":\"Allure.Xunit\",\"async\":true}"), ".json");
            Assert.True(true);
        });
    }
}
