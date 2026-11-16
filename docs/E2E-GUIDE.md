# Guia do padrão E2E de APIs .NET

Este documento especifica o padrão de implementação, organização, execução e evidências dos testes E2E deste boilerplate. Ele deve permitir que outra pessoa ou agente continue o trabalho em uma nova sessão ou implemente o mesmo padrão em um projeto real sem depender da conversa, da pesquisa original ou da leitura de outros documentos.

Os caminhos apresentados são relativos à raiz do repositório. As versões representam a combinação exercitada neste boilerplate em 14/11/2026, não uma recomendação de atualização automática para versões mais recentes. Exemplos de IDs, nomes de domínio e dados de negócio são fictícios.

Roteiro de leitura: [estrutura](#estrutura), [domínio/feature/cenário](#agrupamentos), [stack e Allure](#stack), [configuração](#configuracao), [fixture](#fixture), [jornadas](#jornadas), [evidências](#evidencias), [SQL reproduzível](#sql-reproduzivel), [sample](#sample), [execução](#execucao), [comandos](#comandos), [diagnóstico](#diagnostico), [verificador](#verificador), [CI](#ci), [migração](#migracao), [problemas recorrentes](#problemas) e [aceite](#aceite).

## 1. O resultado que precisa ser preservado

Um cenário E2E exerce uma ação de negócio pela interface pública da aplicação e verifica os efeitos relevantes dessa ação. HTTP aciona ou consulta a API; SQL Server e Redis são instrumentos opcionais de preparação, observação e limpeza. A jornada de referência usa os três. Um cenário somente HTTP não deve exigir connection strings dos clientes SQL/Redis do runner.

Uma execução está completa quando permite responder, pelos artefatos produzidos:

1. Qual domínio, feature e comportamento foram testados?
2. Qual ação pública foi executada e qual resposta foi recebida?
3. Qual estado persistido ou temporário foi observado?
4. Qual consulta SQL foi preparada, quais parâmetros seguros foram enviados e como reproduzi-la?
5. Qual expectativa falhou e em qual step isso aconteceu?
6. A causa observável foi asserção, configuração, timeout ou indisponibilidade?
7. Os dados de quais jornadas foram criados e limpos?
8. O relatório corresponde somente à execução corrente e contém os anexos referenciados?
9. Os segredos ficaram ausentes de logs, resultados brutos, anexos e HTML gerado?

### 1.1. Regras obrigatórias

| Regra | Aplicação |
|---|---|
| Ação de negócio pela API | Não inserir diretamente no SQL o pedido cuja criação pela API está sendo testada. |
| Independência dos cenários | Nenhum teste depende de outro ter criado dados, aquecido cache ou executado primeiro. |
| Dados exclusivos | Gerar identificador próprio por jornada e propagar correlação quando suportado. |
| Evidência antes da asserção | Capturar a observação antes de `Assert`; uma falha não pode impedir a captura. |
| Agrupamentos estáveis | Domínio, feature e cenário não recebem GUID, timestamp, URL ou nome da máquina. |
| Projeções explícitas | Publicar somente campos aprovados e valores seguros; nunca serializar tudo por conveniência. |
| SQL rastreável | Preservar o texto exato do comando aprovado, parâmetros tipados e script de reprodução seguro. |
| Limpeza delimitada | Remover somente registros e chaves cuja propriedade da jornada está comprovada. |
| Falha original preservada | Uma falha secundária de limpeza não substitui o erro que originou o diagnóstico. |
| Execução identificável | Novo diretório de resultados por execução; histórico separado, caso adotado. |
| Falha continua falha | Gerar relatório após erro não pode transformar o resultado do runner em sucesso. |
| Zero testes não é sucesso | Falha de descoberta ou ausência de resultados novos precisa de erro explícito. |

<a id="estrutura"></a>

## 2. Organização física e responsabilidades

### 2.1. Estrutura atual do boilerplate

```text
Boilerplates.sln
global.json
Directory.Build.props
package.json
package-lock.json
allurerc.mjs
e2e.runsettings
compose.yaml
.env.example
.gitignore
.dockerignore
.github/workflows/e2e.yml
scripts/
  e2e.ps1
  sample-env.ps1
  stack.ps1
  verify-artifacts.mjs
samples/
  ReferenceApi/
    ReferenceApi.csproj
    packages.lock.json
    Dockerfile
    Program.cs
    appsettings.json
  sqlserver/init.sql
  wiremock/mappings/
    catalog-product-found.json
    catalog-product-unavailable.json
tests/Boilerplate.E2E/
  Boilerplate.E2E.csproj
  packages.lock.json
  appsettings.json
  allureConfig.json
  xunit.runner.json
  Configuration/E2eSettings.cs
  Fixtures/E2eFixture.cs
  Infrastructure/
    Http/ApiClient.cs
    SqlServer/SqlServerClient.cs
    Redis/RedisClient.cs
  Reporting/
    AllureEvidence.cs
    EvidenceSanitizer.cs
  Scenarios/
    ReferenceJourneyTests.cs
    HttpOnlyTests.cs
  Diagnostics/
    AdapterProofTests.cs
    ConfigurationTests.cs
    AllureFailureTests.cs
docs/
  E2E-GUIDE.md
  research/allure-e2e/          # material original; preservar
  validation/allure-e2e.md     # histórico da validação executada
artifacts/<run-id>/           # gerado; não versionar
```

### 2.2. Organização ao crescer para um projeto real

O sample mantém duas jornadas na mesma classe por ser pequeno. Para um projeto com várias capacidades, organize os testes de negócio por domínio e feature, com nomes de arquivo que expressem o comportamento:

```text
tests/MinhaApi.E2E/
  Configuration/
  Fixtures/
  Infrastructure/
    Http/
    SqlServer/
    Redis/
  Reporting/
  Contracts/                         # DTOs próprios do contrato público, se necessários
  Scenarios/
    Pedidos/                         # domínio
      Criacao/                       # feature
        CriarPedidoComPrecoExternoTests.cs
        RejeitarCatalogoIndisponivelTests.cs
      Consulta/
        ConsultarPedidoComCacheTests.cs
    Pagamentos/
      Confirmacao/
        ConfirmarPagamentoTests.cs
  Diagnostics/
```

Essa árvore é uma convenção para a migração, não uma afirmação de que essas subpastas já existem no sample. Um arquivo pode conter mais de um teste quando eles expressarem variações pequenas do mesmo comportamento. Ao acumular comportamentos diferentes, separe-os. Evite arquivos organizados apenas por verbos técnicos, como `PostTests` ou `RedisTests`, para jornadas de negócio.

Pastas e namespaces ajudam a localizar o código, mas **não substituem os labels explícitos do Allure**. Mover um arquivo de pasta não deve mudar silenciosamente o domínio ou a feature no relatório.

### 2.3. Divisão de responsabilidades

| Camada | Deve conter | Deve evitar |
|---|---|---|
| `Configuration` | Carregamento, precedência, mapeamento tipado, validação por dependência. | Abrir conexões ou escrever valores secretos em erros. |
| `Fixtures` | Recursos compartilháveis e seu descarte; dependências opcionais sob demanda. | IDs e estado mutável de uma jornada armazenados globalmente. |
| `Infrastructure/Http` | Envio real, cancelamento, limites, captura de resposta segura. | Asserções de negócio e publicação automática de payload bruto. |
| `Infrastructure/SqlServer` | Consultas específicas, parâmetros, pooling, descarte, evidência do comando. | Executor público de SQL arbitrário e interpolação de dados na consulta executada. |
| `Infrastructure/Redis` | Chaves de propriedade conhecida, leitura, TTL e exclusão delimitada. | Multiplexer por operação, `FLUSHDB` e varredura global para limpar. |
| `Reporting` | Steps, anexos, projeções permitidas, limites e diagnóstico sanitizado. | Considerar `Masked`/`Hidden` uma forma de remover segredos dos artefatos. |
| `Scenarios` | Sequência da jornada, expectativas públicas e comparação dos efeitos. | Reimplementar cliente HTTP, driver SQL ou mecanismo de relatório em cada teste. |
| `Diagnostics` | Prova do adaptador, testes de configuração/sanitização e falha intencional opt-in. | Misturar falhas intencionais com testes de erro de negócio que devem passar. |
| `scripts` | Entrada compartilhada entre local e CI, geração e verificação dos artefatos. | Regras de negócio ou scripts diferentes de geração em cada ambiente. |
| `samples` | Ambiente demonstrativo reproduzível, com dependências e mocks controlados. | Ser uma dependência obrigatória para testar uma API real. |

Use APIs pequenas. Os contratos de referência são:

```csharp
Task<ApiResponse> ApiClient.SendAsync(
    HttpMethod method, string path, Guid correlation,
    object? body = null, CancellationToken cancellation = default);
// ApiResponse: int Status, string Body. Body é material de asserção, não de publicação irrestrita.

Task<OrderRow?> SqlServerClient.FindOrderAsync(Guid id, CancellationToken cancellation);
Task<int> SqlServerClient.DeleteOrderAsync(Guid id, CancellationToken cancellation);
// OrderRow: Guid Id, Guid Reference, string Sku, decimal Price, string Currency.

Task<CacheObservation> RedisClient.ObserveOrderAsync(Guid id, CancellationToken cancellation);
Task<bool> RedisClient.DeleteOrderAsync(Guid id, CancellationToken cancellation);
// CacheObservation: bool Present, string? Value, TimeSpan? Ttl.
```

Ao migrar, substitua os contratos de pedido pelos do domínio real. Preserve a separação entre observação retornada ao teste e projeção segura publicada no relatório.

<a id="agrupamentos"></a>

## 3. Agrupamentos: domínio → feature → cenário

### 3.1. Mapeamento no Allure

| Conceito do padrão | Label Allure | Pergunta respondida | Exemplo |
|---|---|---|---|
| Domínio | `epic` | Qual área de negócio está sendo exercitada? | `Pedidos` |
| Feature | `feature` | Qual capacidade dessa área? | `Criação de pedidos` |
| Cenário | `story` | Qual comportamento observável deve ocorrer? | `Persistir pedido com preço retornado pelo catálogo` |
| Operação da jornada | Step | Qual ação ou verificação ocorreu dentro do cenário? | `Conferir pedido persistido no SQL Server` |

O mapeamento para domínio/capacidade/comportamento é uma decisão deste padrão. O relatório deve usar `groupBy: ["epic", "feature", "story"]`. Os labels automáticos `parentSuite`, `suite`, `subSuite`, classe e namespace podem existir; a árvore principal deve continuar baseada nos três labels de negócio.

Domínios técnicos também são válidos para testes de suporte. Por exemplo: `Plataforma → Relatórios → Descobrir teste, aguardar step e anexar JSON`. Não agrupe todos os testes de negócio sob `API`, `E2E` ou o nome do projeto: isso elimina informação útil.

### 3.2. Exemplo de declaração

```csharp
using Allure.Net.Commons;
using Allure.Xunit.Attributes;
using Xunit;

[AllureEpic("Pedidos")]
[AllureFeature("Criação de pedidos")]
[AllureTag("http", "sqlserver", "redis")]
public sealed class CriarPedidoComPrecoExternoTests
{
    [Fact]
    [Trait("Category", "Journey")]
    [AllureStory("Persistir pedido com preço retornado pelo catálogo")]
    [AllureSeverity(SeverityLevel.critical)]
    public async Task Deve_persistir_preco_e_moeda_do_catalogo()
    {
        await AllureApi.Step("Criar pedido pela API", async () =>
        {
            await Task.Yield(); // Neste exemplo de labels; na jornada, enviar a chamada real.
        });
    }
}
```

O exemplo acima demonstra agrupamentos; não é um teste E2E completo. Os exemplos completos de sequência e aceite estão descritos nas próximas seções.

### 3.3. Regras de nomes, tags e identidade

- Dê ao cenário um nome de comportamento com resultado observável. `Deve_rejeitar_catalogo_indisponivel_sem_persistir_pedido` é mais útil que `Teste02`.
- Mantenha `epic`, `feature`, `story`, nome do método e nomes dos steps estáveis entre rodadas. IDs variáveis pertencem ao conteúdo sanitizado dos anexos.
- Use um vocabulário consistente. Não alterne `Pedidos`, `Pedido` e `Orders` para o mesmo domínio no mesmo relatório.
- Uma jornada pode ter vários steps; não transforme cada step HTTP/SQL/Redis em outra `story`.
- Em geral, um `[Fact]` representa um cenário. Use `[Theory]` para variações com o mesmo comportamento e somente parâmetros publicáveis; o adaptador pode capturar parâmetros automaticamente.
- `Trait("Category", ...)` seleciona o conjunto executado pelo runner. `AllureTag` auxilia a leitura do relatório. São mecanismos distintos.
- Categorias atuais: `Journey`, `Http`, `Proof`, `Configuration`, `Diagnostic`. Os filtros do script precisam corresponder exatamente a esses valores ou às substituições adotadas na migração.
- Aplique uma severidade no método, evitando depender de precedência entre atributos de classe e método. Use impacto real; não classifique todos os testes como blocker.
- Adicione responsável e links somente quando existirem dados reais de equipe, requisito ou incidente. Não invente nomes, tickets ou URLs para preencher o relatório.
- Preservar nomes ajuda a comparar resultados, mas mudanças de classe, namespace e método podem mudar a identidade calculada pelo adaptador. Trate reorganizações como alterações deliberadas de identidade quando adotar histórico.

<a id="stack"></a>

## 4. Stack e configuração mínima para reimplementar

### 4.1. Combinação exercitada

| Componente | Versão / escolha |
|---|---|
| SDK observado | .NET SDK 10.0.404 |
| Target framework | `net10.0` |
| xUnit | 2.9.3 |
| Runner | `xunit.runner.visualstudio` 2.8.2, via VSTest |
| Test SDK | Microsoft.NET.Test.Sdk 17.14.1 |
| Adaptador | Allure.Xunit 2.15.0; Allure.Net.Commons 2.15.0 transitivo |
| SQL client | Microsoft.Data.SqlClient 6.1.8 |
| Redis client | StackExchange.Redis 2.9.32 |
| Gerador | Pacote npm `allure` 3.33.0 |
| Node final observado | 24.22.3 |
| Shell local observado | Windows PowerShell 5.1 |
| Shell configurado no CI | PowerShell 7 (`pwsh`) em Ubuntu |
| Docker observado | Engine 29.9.2; Compose 5.7.0; containers Linux |

O adaptador xUnit v2 foi exercitado com runner 2.8.2. Não atualize isoladamente esse runner para 3.x. A combinação xUnit v3 + Allure.Xunit.v3 + Microsoft Testing Platform não foi validada aqui: uma migração para ela exige nova prova de descoberta, async, anexos, status de asserção e geração do relatório.

Antes de alterar a stack, preserve a combinação compatível do projeto real quando possível. Não imponha `net10.0` a uma solução existente sem avaliar seus SDKs, frameworks e gerenciamento central de pacotes. As invariantes deste guia continuam valendo se outra combinação for comprovada.

### 4.2. Arquivos de build

`global.json` do boilerplate:

```json
{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }
```

Isso permite selecionar uma feature band instalada de .NET 10; não fixa exatamente o SDK observado 10.0.404. O registro da execução deve informar o SDK efetivo.

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  </PropertyGroup>
</Project>
```

Projeto de testes mínimo equivalente:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsTestProject>true</IsTestProject>
    <IsPackable>false</IsPackable>
    <RunSettingsFilePath>$(MSBuildProjectDirectory)/../../e2e.runsettings</RunSettingsFilePath>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" PrivateAssets="all" />
    <PackageReference Include="Allure.Xunit" Version="2.15.0" />
    <PackageReference Include="Microsoft.Data.SqlClient" Version="6.1.8" />
    <PackageReference Include="StackExchange.Redis" Version="2.9.32" />
  </ItemGroup>
  <ItemGroup>
    <None Update="appsettings*.json;allureConfig.json;xunit.runner.json"
          CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

Se o projeto real usar `Directory.Packages.props`, declare as versões lá e mantenha referências sem versão no `.csproj`, respeitando a convenção da solução. Adicione o projeto à solução existente e preserve os arquivos de lock apropriados. Use `dotnet restore <solução> --locked-mode` no caminho reproduzível após gerar os locks.

`package.json` precisa de `private: true` e `devDependencies: { "allure": "3.33.0" }`. Gere e versione `package-lock.json`; use `npm ci` nas próximas instalações. Não use instalação global como fonte de versão do gerador. O comando de geração deste padrão executa `node node_modules/allure/cli.js`.

### 4.3. Adaptador e gerador têm configurações diferentes

`e2e.runsettings` seleciona o reporter do runner:

```xml
<RunSettings>
  <RunConfiguration>
    <ReporterSwitch>allure</ReporterSwitch>
  </RunConfiguration>
</RunSettings>
```

`tests/<projeto>/allureConfig.json` configura o adaptador .NET:

```json
{
  "allure": {
    "directory": "allure-results",
    "failExceptions": ["Xunit.Sdk.XunitException"],
    "xunitRunnerReporter": "none"
  }
}
```

Esse arquivo é o fallback para execução direta. O script oficial deve escrever outro JSON equivalente, com `directory` absoluto e exclusivo da rodada, e apontar `ALLURE_CONFIG` para esse arquivo antes de iniciar o runner. O fallback pode acumular resultados entre execuções diretas; não o use para atestar isolamento de rodadas.

`xunit.runner.json` configura o runner, não o gerador:

```json
{
  "$schema": "https://xunit.net/schema/current/xunit.runner.schema.json",
  "parallelizeTestCollections": true
}
```

`allurerc.mjs` configura o Allure Report 3:

```javascript
import { defineConfig } from "allure";

export default defineConfig({
  name: "API E2E",
  plugins: {
    awesome: {
      import: "@allurereport/plugin-awesome",
      options: {
        reportName: "API E2E · HTTP, SQL Server e Redis",
        reportLanguage: "en",
        groupBy: ["epic", "feature", "story"],
        singleFile: false,
      },
    },
  },
});
```

Passe `--config allurerc.mjs` explicitamente. A configuração do gerador não deve ser confundida com `allureConfig.json`. Não habilite histórico, retries, categorias, quality gate, outro idioma, HTML único ou uma opção presumida de analytics sem necessidade e validação próprias. Essas opções não fazem parte da linha de base exercitada.

<a id="configuracao"></a>

## 5. Configuração dos três pontos de acesso

O arquivo versionado `tests/<projeto>/appsettings.json` deve conter estas três chaves raiz, com strings de conexão vazias:

```json
{
  "baseurl": "http://localhost:5080",
  "sqlserver-connectionstring": "",
  "redis-connectionstring": ""
}
```

### 5.1. Carregamento e precedência

Implemente o seguinte algoritmo:

1. Resolver os caminhos por `AppContext.BaseDirectory`, nunca pelo diretório corrente do shell.
2. Carregar `appsettings.json`, obrigatório.
3. Sobrepor `appsettings.local.json`, se existir, preservando as chaves não informadas nele.
4. Sobrepor variáveis de ambiente individualmente, com mapeamento explícito da tabela abaixo.
5. Construir um objeto tipado `E2eSettings(Uri BaseUrl, string SqlServerConnectionString, string RedisConnectionString)`.
6. Validar URL na carga; validar SQL e Redis somente quando o cenário pedir o componente.

| Chave JSON | Variável de ambiente |
|---|---|
| `baseurl` | `E2E_BASEURL` |
| `sqlserver-connectionstring` | `E2E_SQLSERVER_CONNECTIONSTRING` |
| `redis-connectionstring` | `E2E_REDIS_CONNECTIONSTRING` |

Precedência: **JSON base < JSON local < ambiente**. No carregador, um override que exista como string vazia prevalece e deve provocar a validação de ausência quando o componente for usado; não substituí-lo silenciosamente pelo valor do JSON. Alguns shells removem variáveis ao atribuir vazio; nesse caso, não existe override de ambiente e aplica-se o JSON. O helper opcional `-Sample` preenche valores de conexão que estejam ausentes/vazios; não o use para o teste negativo de configuração ausente.

Copie os JSONs para a saída pelo projeto. Ignore no Git `**/appsettings.local.json` e `.env`; exclua-os também do contexto Docker. Não anexe o objeto `E2eSettings`, pois ele contém credenciais.

### 5.2. Validações exigidas

- `BaseUrl`: URI absoluta, esquema HTTP ou HTTPS, sem userinfo, query ou fragmento. A mensagem de erro deve indicar `baseurl / E2E_BASEURL` sem repetir o valor recebido.
- SQL: exigir conexão somente em cenário que chame SQL; fazer parse com `SqlConnectionStringBuilder`; exigir servidor e banco. Parse válido não comprova autenticação ou disponibilidade.
- Redis: exigir conexão somente quando usado; fazer parse com `ConfigurationOptions.Parse`; exigir endpoint e configurar os limites descritos neste guia.
- Em erro de JSON, parse, autenticação ou conexão, produzir mensagem acionável sem a connection string, senha, URL completa com credenciais ou inner exception original.
- Não usar `EnsureSuccessStatusCode` antes de registrar evidência HTTP quando um erro de status faz parte do contrato testado.

Variáveis adicionais do sample:

| Nome | Finalidade |
|---|---|
| `SQLSERVER_SA_PASSWORD` | Senha local do SQL Server; lida de `.env` ou ambiente para iniciar o sample. |
| `E2E_WIREMOCK_ADMIN_URL` | Observação opcional do journal do mock; não é um quarto ponto obrigatório de negócio. |
| `E2E_SYNTHETIC_SECRET` | Segredo fictício usado para comprovar a sanitização dos artefatos. |
| `E2E_DIAGNOSTIC` | `1` habilita a descoberta executável do diagnóstico intencional. |
| `ALLURE_CONFIG` | Arquivo efetivo de configuração do adaptador para a rodada. |

O `Authorization: Bearer ...` que o sample envia com o segredo sintético **não implementa autenticação de produção**. Ao migrar, implemente a autenticação real em componente próprio, mantenha tokens fora dos anexos e preserve um teste adversarial separado para sanitização. Não envie o canário como se fosse credencial real de uma API autenticada.

<a id="fixture"></a>

## 6. Fixture, recursos e prazos

Use `IClassFixture<E2eFixture>` como padrão inicial. A fixture implementa `Xunit.IAsyncLifetime`, cria um `HttpClient` reutilizado pela classe e permite obter SQL/Redis somente sob demanda.

| Recurso | Política de ciclo de vida | Limite atual |
|---|---|---|
| HTTP | Um `HttpClient` por fixture; descartar no fim da fixture. | Dez segundos por operação, incluindo leitura do corpo. |
| SQL Server | Conexão por operação; pooling do driver; descartar conexão/comando/reader. | Connect timeout e command timeout de cinco segundos. |
| Redis | Um `ConnectionMultiplexer` reutilizado por fixture, criado por `Lazy<Task<...>>`. | Connect/async/sync timeout de cinco segundos, connect retry 1. |
| Jornada | Token de cancelamento próprio. | 45 segundos para a sequência de negócio, depois da obtenção das dependências. |
| Limpeza | Token independente, inclusive após a expiração da jornada. | 15 segundos para tentar os recursos próprios. |

Na fixture, `InitializeAsync` pode retornar `Task.CompletedTask`: isso permite cenários somente HTTP sem inicializar SQL/Redis. `Sql()` valida a configuração e entrega um cliente pequeno; `RedisAsync()` valida opções e aguarda o multiplexer compartilhado. `DisposeAsync()` descarta o HTTP e o multiplexer que a fixture efetivamente criou; não encerra infraestrutura que pertence ao ambiente externo.

Para HTTP, combine o token do chamador com um token de dez segundos e passe esse token tanto para `SendAsync(..., ResponseHeadersRead, token)` quanto para a leitura do stream. Somente `HttpClient.Timeout` não representa a política completa de leitura quando se usa `ResponseHeadersRead`.

Para Redis, configure `AbortOnConnectFail=true` e `IncludeDetailInExceptions=false` no cliente de testes. `WaitAsync(token)` cancela a espera da aplicação, não cancela um comando Redis que já foi enviado. Não presuma que timeout de escrita significa ausência do efeito. As operações de limpeza e a verificação posterior precisam considerar esse estado incerto.

Métodos na mesma classe xUnit v2 são serializados. Classes em coleções distintas podem executar em paralelo. Compartilhar uma fixture de coleção só é justificável quando houver recurso que precise desse escopo; documente a escolha e não use serialização global para esconder colisões de dados.

<a id="jornadas"></a>

## 7. Desenho de uma jornada

### 7.1. Roteiro antes de escrever o teste

Registre domínio, feature, cenário, pré-condições, ação pública, resposta esperada, efeitos observáveis, política de consistência, dados exclusivos, limpeza e evidências. A matriz abaixo especifica o sample completo:

| Fase | Sucesso | Catálogo indisponível |
|---|---|---|
| Identidade | Novo GUID por teste; id e reference são iguais apenas neste sample. | Outro GUID, exclusivo do cenário. |
| Mock | `GET /catalog/products/SKU-E2E` → 200, preço 42.50 e moeda BRL. | `GET /catalog/products/SKU-UNAVAILABLE` → 503. |
| Ação | `POST /orders` com reference e SKU-E2E. | `POST /orders` com reference e SKU-UNAVAILABLE. |
| Resposta | 201; contrato de pedido com id, reference, sku, price e currency. | 502; `code=catalog_unavailable`. |
| SQL | Buscar pelo id retornado; uma linha, referência correta, preço/moeda do mock. | Nenhuma linha para o identificador exclusivo. |
| Cache anterior | Chave ainda ausente. | Ausente. |
| Consulta pública | `GET /orders/{id}` → 200 com contrato consistente; leitura popula cache. | `GET /orders/{reference}` → 404; não popula cache. |
| Redis | `e2e:orders:{id:D}` presente; campos iguais aos consultados; `0 < TTL <= 300`. | Chave correspondente ausente. |
| Journal opcional | Exatamente uma chamada do SKU com X-Correlation-Id próprio. | Exatamente uma chamada do SKU de falha com a correlação própria. |
| Finalização | Exclusão SQL e Redis restrita à jornada. | Mesma limpeza delimitada, mesmo que não haja recursos. |

O cenário de catálogo indisponível é um **teste que passa** quando a aplicação trata a falha corretamente. Ele não substitui o diagnóstico de asserção intencional.

### 7.2. Estrutura de steps

```text
Pedidos
  Integração com catálogo e consulta
    Persistir preço externo e popular cache na consulta pública
      Criar pedido pela API
        Operação HTTP.json
      Conferir pedido persistido no SQL Server
        Comando SQL Server.json
        Reproduzir consulta SQL Server (somente leitura).sql
        Pedido persistido.json
      Conferir cache vazio antes da consulta pública
        Pedido em cache.json
      Consultar pedido pela API
        Operação HTTP.json
      Conferir pedido em cache no Redis
        Pedido em cache.json
      Conferir chamada externa correlacionada no WireMock
        Projeção do journal.json
      Limpar somente recursos desta jornada
        Comando SQL Server.json
        Reproduzir limpeza SQL Server (ALTERA DADOS).sql
        Limpeza SQL Server.json
        Limpeza Redis.json
        Resultado da limpeza.json
```

O journal não aparece quando sua observação está desabilitada. Os nomes acima são nomes de apresentação; os nomes físicos dos arquivos de anexo são gerados pelo adaptador e não devem ser renomeados depois da execução.

### 7.3. Exemplo completo de cenário exclusivamente HTTP

O exemplo pressupõe que `E2eFixture` e `ApiClient` implementem os contratos e a política de evidências especificados neste guia. O método do cliente captura a evidência HTTP antes de devolver `ApiResponse`.

```csharp
using Allure.Net.Commons;
using Allure.Xunit.Attributes;
using Xunit;
using MinhaApi.E2E.Fixtures;

namespace MinhaApi.E2E.Scenarios.Plataforma.Prontidao;

[AllureEpic("Plataforma")]
[AllureFeature("Prontidão")]
public sealed class ConsultarProntidaoTests(E2eFixture fixture) : IClassFixture<E2eFixture>
{
    [Fact, Trait("Category", "Http")]
    [AllureStory("Consultar saúde sem configurar clientes SQL Server ou Redis")]
    public async Task Deve_responder_pronto()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await AllureApi.Step("Consultar prontidão por HTTP", async () =>
        {
            var response = await fixture.Api.SendAsync(
                HttpMethod.Get, "/health/ready", Guid.NewGuid(), cancellation: deadline.Token);
            Assert.Equal(200, response.Status);
        });
    }
}
```

Para uma jornada completa, a sequência é a da matriz da seção 7.1. Retenha o DTO seguro retornado pela consulta pública e compare o cache com ele; não compare apenas existência da chave. Registre o identificador retornado antes de asserções que possam falhar. Leia contratos com um parser que produza erro seguro se o JSON não corresponder ao esperado; não coloque o body bruto na mensagem de falha.

### 7.4. Isolamento, eventual consistência e limpeza

Gere a referência antes da ação HTTP. Se a API real atribuir outro id, registre a relação entre referência, id retornado e chaves criadas. A igualdade `id=reference` e o predicado de limpeza do sample não podem ser copiados para um sistema em que essa igualdade não exista.

Para testar processamento assíncrono real, faça polling somente da observação relevante, com prazo total definido, intervalo controlado e cancelamento. Guarde quantidade de tentativas, última projeção e tempo decorrido. Não acrescente `Thread.Sleep` fixo nem repita a ação de negócio para tentar obter um estado favorável. A linha de base do sample é síncrona e não precisa desse polling.

Algoritmo de limpeza equivalente ao sample:

```text
obter e validar clientes necessários
gerar referência exclusiva
failed = false
try:
    executar jornada com prazo de 45 segundos
catch:
    failed = true
    propagar a exceção original
finally:
    criar prazo novo de 15 segundos
    errors = 0
    abrir step "Limpar somente recursos desta jornada"
    tentar excluir pedido próprio; se falhar, incrementar errors e registrar falha segura
    tentar excluir chave própria; se falhar, incrementar errors e registrar falha segura
    anexar { referência segura, errors, originalFailurePreserved: failed }
    se errors > 0 e failed == false:
        falhar por limpeza incompleta
    se já havia falha:
        manter a exceção original
```

Não use o token já expirado da jornada para a limpeza. Uma consulta SQL ou escrita Redis com timeout pode ter sido enviada: a limpeza deve usar a referência criada antes da chamada para localizar somente os recursos dessa jornada. Se a API puder criar vários registros, mantenha um inventário por cenário. Não habilite limpeza por prefixo compartilhado, intervalo de datas ou "todos os registros de teste".

<a id="evidencias"></a>

## 8. Contrato de evidências

### 8.1. Conteúdo mínimo por origem

| Origem | Publicar | Não publicar |
|---|---|---|
| HTTP | Método, rota aprovada sem query, status, correlação segura e projeção de resposta. | Authorization, cookies, tokens, query strings ou body bruto. |
| SQL | Operação, texto exato do template aprovado, parâmetros tipados aprovados, replay seguro, estado da execução, quantidade e campos observados relevantes. | Connection string, senha, SQL dinâmico com valores embutidos ou parâmetros sensíveis sem transformação. |
| Redis | Operação, chave construída com identificador seguro ou referência sanitizada, presença, campos aprovados e TTL quando exigido. | String de conexão, credenciais, valor completo não revisado ou chave que exponha dado pessoal. |
| Integração externa | Método/rota, correlação e contagem/status projetados do journal. | Journal inteiro, headers e respostas não filtradas. |
| Falha | Componente, classe segura de erro, códigos aprovados e distinção entre asserção e operação. | `exception.ToString()`, mensagem/inner exception originais que possam conter endpoints ou credenciais. |
| Limpeza | Recursos próprios, quantidade de exclusões, erros e preservação da falha original. | Inventário de recursos de outras jornadas ou do ambiente inteiro. |

### 8.2. Ordem e vinculação

A ordem é sempre **operar → projetar → anexar no step ativo → afirmar**. Para SQL, há ainda uma captura do comando preparado antes de enviá-lo, descrita na seção 9. Os anexos não devem ser adicionados ao teste inteiro depois que o step já terminou.

Use `await AllureApi.Step("Nome estável", async () => { ... })`. Não deixe tasks em segundo plano ou callbacks sem `await` escreverem evidência depois do encerramento do contexto. Um `AllureApi.Step("Nome")` sem lambda é um registro pontual; ele não cria um contexto aberto para anexos posteriores.

Use a API runtime nos exemplos de clientes para controlar explicitamente os dados publicados. Atributos automáticos em métodos com parâmetros podem capturar argumentos; não aplique esse mecanismo a connection strings, tokens ou payloads completos sem uma política de exclusão comprovada.

### 8.3. Serialização, volume e dados inesperados

`AllureEvidence.Json` recebe exclusivamente uma projeção já aprovada, serializa UTF-8, usa `application/json` e extensão `.json`. Ele limita o anexo a 8 KiB; acima disso, substitui o conteúdo inteiro por JSON válido como:

```json
{ "truncated": true, "reason": "evidence exceeds 8 KiB" }
```

`AllureEvidence.Text` recebe somente texto já aprovado, usa `text/plain` e extensão `.txt` ou `.sql`. Acima de 8 KiB, substitui o conteúdo por uma indicação de omissão; um script truncado não deve ser apresentado como executável.

O cliente HTTP lê no máximo 16 KiB mais um byte para detectar excesso. Se o limite for excedido, descarta o corpo para o contrato do cliente, marca `truncated=true` e conserva método/rota/status/correlação. O parser de projeção também limita a entrada; corpo ilegível, ausente ou com formato inesperado produz descrição segura. Não faça dump bruto para diagnosticar o parser.

A projeção atual permite apenas:

- `id` e `reference`: valores parseáveis como GUID.
- `price`: decimal entre 0 e 1.000.000.
- `currency`: BRL, USD ou EUR.
- `sku`: SKU-E2E ou SKU-UNAVAILABLE.
- `code`: catalog_unavailable, not_found ou ready.

O cliente retorna material para asserção, mas o relatório recebe a projeção permitida. Na migração, defina a lista de campos, tipos, intervalos e enumerações do contrato real. Um campo de nome permitido não torna qualquer valor automaticamente seguro. Trate a rota e a chave Redis com a mesma disciplina.

Use asserções sobre GUIDs, números e valores previamente validados. Ao falhar sobre texto não confiável, prefira uma expectativa booleana e mensagem fixa segura; alguns asserts imprimem o valor real automaticamente. Não passe segredos a nomes de testes, atributos, `[Theory]` ou títulos de steps.

### 8.4. Prova de sanitização

Defina `E2E_SYNTHETIC_SECRET` com um valor fictício identificável. Faça-o atravessar o caminho real de captura: headers e uma entrada adversarial com query, campos de resposta, erros e conteúdo descartado. Valide a projeção e depois procure o segredo em todos os arquivos da rodada, incluindo logs UTF-8/UTF-16, resultados, anexos e arquivos do relatório.

O scanner atual usa o segredo do ambiente ou `local-evidence-canary` como fallback e também verifica `SQLSERVER_SA_PASSWORD` quando disponível. Ele procura bytes UTF-8 e UTF-16LE. Se a migração introduzir serialização escapada, codificação, compactação ou anexos binários, estenda a inspeção para essas representações; não confunda ausência de bytes literais com prova universal de ausência do dado.

`Masked`, `Hidden` e ocultação de labels são recursos de apresentação. A proteção precisa acontecer antes de serializar ou anexar. O adaptador pode adicionar host, thread, linguagem e framework automaticamente; revise esses metadados conforme a política do ambiente, sem presumir que ocultar a UI remove o conteúdo bruto.

<a id="sql-reproduzivel"></a>

## 9. Evidência SQL com consulta exata e reprodução direta

Esta seção é obrigatória para cada nova observação SQL. Um anexo com apenas `operation=FindOrderById` não é suficiente para reproduzir a consulta na base.

### 9.1. O que registrar

Para cada comando aprovado, gere um anexo JSON `Comando SQL Server` com:

| Campo | Significado |
|---|---|
| `operation` | Nome estável da consulta ou comando de limpeza. |
| `executionState` | `prepared` antes do envio; não afirma sucesso da execução. |
| `commandType` | `Text` no sample. Stored procedures precisam de representação específica. |
| `commandText` | Conteúdo exato de `SqlCommand.CommandText`, sem reconstrução aproximada. |
| `parameters` | Nome, tipo SQL e valor seguro de cada `SqlParameter` efetivamente configurado. |
| `commandTimeoutSeconds` | Limite efetivo do comando. |
| `readOnly` | Classificação do template revisado; não inferir somente pelo primeiro verbo textual. |
| `reproducible` | `true` somente se todos os dados necessários publicados forem seguros e completos. |
| `replaySql` | Declarações tipadas dos parâmetros seguidas pelo comando exato. |

Anexe também `replaySql` em arquivo `.sql` com MIME `text/plain`, no mesmo step. Ele deve ser baixável ou copiável sem extrair texto de JSON. Use nomes que distingam **somente leitura** de **ALTERA DADOS**.

O objeto deve ser derivado do `SqlCommand` e de seus parâmetros reais. No sample, aceite somente os dois textos constantes abaixo, `CommandType.Text`, exatamente um parâmetro chamado `@id`, tipo `SqlDbType.UniqueIdentifier` e valor C# `Guid`. Se qualquer condição deixar de ser verdadeira, falhe com mensagem sanitizada de comando sem política de evidência aprovada. Não implemente serialização automática de qualquer comando do driver.

### 9.2. Exemplo de anexo da consulta de observação

```json
{
  "operation": "FindOrderById",
  "executionState": "prepared",
  "commandType": "Text",
  "commandText": "SELECT Id, Reference, Sku, Price, Currency FROM dbo.Orders WHERE Id = @id",
  "parameters": [
    {
      "name": "@id",
      "sqlType": "uniqueidentifier",
      "value": "36c515da-e06c-43ed-9c30-f64b99002c64"
    }
  ],
  "commandTimeoutSeconds": 5,
  "readOnly": true,
  "reproducible": true,
  "replaySql": "-- SOMENTE LEITURA: reproduz a observação do teste.\n-- Selecione a mesma base configurada para o cenário; nenhuma credencial é incluída.\nDECLARE @id uniqueidentifier = '36c515da-e06c-43ed-9c30-f64b99002c64';\nSELECT Id, Reference, Sku, Price, Currency FROM dbo.Orders WHERE Id = @id;\n"
}
```

O arquivo de reprodução equivalente é:

```sql
-- SOMENTE LEITURA: reproduz a observação do teste.
-- Selecione a mesma base configurada para o cenário; nenhuma credencial é incluída.
DECLARE @id uniqueidentifier = '36c515da-e06c-43ed-9c30-f64b99002c64';
SELECT Id, Reference, Sku, Price, Currency FROM dbo.Orders WHERE Id = @id;
```

A consulta enviada pelo cliente permanece parametrizada:

```csharp
const string query = "SELECT Id, Reference, Sku, Price, Currency FROM dbo.Orders WHERE Id = @id";
await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync(cancellation);
await using var command = new SqlCommand(query, connection) { CommandTimeout = 5 };
command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
// Capturar comando e replay aprovados no step ativo, antes de ExecuteReaderAsync.
await using var reader = await command.ExecuteReaderAsync(cancellation);
// Ler resultado, projetar e anexar execução concluída antes da asserção do cenário.
```

O `DECLARE` existe somente no artefato de reprodução. Não substitua a execução parametrizada por interpolação de strings no cliente.

Depois de ler o resultado, anexe `Pedido persistido` com `executionState=completed`, id seguro, `rows=0` ou `1`, expectativa e observação. Para o sucesso, a expectativa é preço 42.50 e moeda BRL. Para ausência esperada, deixe claro que a expectativa de quantidade é zero; preço/moeda só são pertinentes se houver uma linha.

Se a abertura de conexão falhar, nenhum comando desse cliente foi preparado/enviado naquela operação e pode haver somente a falha de conexão. Se ocorrer erro depois da captura `prepared`, o anexo registra a tentativa; sem o resultado `completed`, a conclusão da operação não foi confirmada. Não transforme timeout em afirmação de que nada aconteceu na base.

### 9.3. Limpeza também deve ser rastreável

O comando exato de limpeza do sample é:

```sql
-- ALTERA DADOS: limpeza do recurso desta jornada; execute apenas se desejar removê-lo.
-- Selecione a mesma base configurada para o cenário; nenhuma credencial é incluída.
DECLARE @id uniqueidentifier = '36c515da-e06c-43ed-9c30-f64b99002c64';
DELETE FROM dbo.Orders WHERE Id = @id AND Reference = @id;
```

Registre `readOnly=false`, operação `DeleteOwnedOrder` e os mesmos detalhes de parâmetro. O resultado concluído registra a quantidade de linhas removidas. Nunca execute automaticamente os `.sql` do relatório para verificá-los: alguns alteram dados. A validação automatizada deve comparar conteúdo e parâmetros; só execute um replay de leitura quando isso fizer parte de uma verificação explícita em ambiente controlado.

### 9.4. Como reproduzir na base

1. Abra o cenário no Allure e localize o step SQL que deseja investigar.
2. Confira `operation`, `executionState`, expectativa, resultado e classificação de leitura/escrita.
3. Baixe o anexo `.sql` do mesmo step ou copie `replaySql`.
4. Conecte-se por seu cliente SQL usando as credenciais fornecidas pelo ambiente, fora do relatório.
5. Selecione a mesma base e confirme o schema. No sample, o banco é `BoilerplateE2E` e a tabela é `dbo.Orders`.
6. Execute o script de leitura aprovado. Não cole uma connection string em anexos nem acrescente senha ao script.
7. Compare o resultado com a observação do momento do teste, considerando a limpeza e a passagem do tempo.

**A limpeza automática normalmente já removeu o registro quando o relatório é aberto.** O replay pode retornar zero linhas apesar de o teste ter observado uma linha antes. O anexo de observação é a evidência daquele instante; o script reproduz a consulta, não restaura o estado histórico. Não desabilite limpeza por padrão para manter o banco como arquivo de evidências.

### 9.5. Parâmetros sensíveis e contratos diferentes

GUID de correlação gerado pelo próprio teste é publicável neste sample. Isso não autoriza publicar CPF, e-mail, token, documento, segredo ou identificador real de cliente como parâmetro SQL.

Para novos tipos, registre tipo completo, incluindo tamanho de texto, precisão/escala decimal e convenção de data, e revise cada valor permitido. Não use simples `string.Replace` para substituir parâmetros por literais: nomes podem se sobrepor, aparecer em comentários ou em strings, e a conversão de tipos pode mudar a semântica.

Se a consulta exigir um valor sensível, preserve o texto do template somente se ele não contiver literais sensíveis, remova o valor antes da serialização e publique um placeholder explícito no replay, com `reproducible=false`. Exemplo:

```sql
-- REQUER PREENCHIMENTO LOCAL: valor sensível omitido do relatório.
DECLARE @email nvarchar(256) = N'<PREENCHER_LOCALMENTE>';
-- Em seguida, o template estático revisado que utiliza @email.
```

Nesse caso, a reprodução integral exige preencher o valor localmente por canal autorizado. Não prometa reprodução automática irrestrita e não sacrifique a sanitização para obtê-la. Projeção de SQL é uma política explícita por operação; não é um logger universal de comandos.

<a id="sample"></a>

## 10. Ambiente de referência com quatro serviços

O sample permite comprovar o padrão sem depender de uma API externa real. A API faz a chamada ao catálogo; o runner não deve substituir essa integração por uma chamada direta ao mock para realizar o negócio.

| Serviço | Implementação | Interno Compose | Host |
|---|---|---|---|
| API | ASP.NET Core, imagem construída do sample | `http://api:8080` | `http://localhost:5080` |
| SQL Server | `mcr.microsoft.com/mssql/server:2022-latest` | `sqlserver,1433` | `tcp:127.0.0.1,14333` |
| Redis | `redis:7-alpine` | `redis:6379` | `localhost:16379` |
| Catálogo simulado | `sheyenrath/wiremock.net:latest` | `http://wiremock:80` | `http://localhost:9090` |

Use essas tags ao reproduzir este sample. Elas são mutáveis; registre os digests observados quando executar em outro momento. Construa somente a imagem da API. Publique portas apenas no loopback local. O endereço IPv4 explícito do SQL resolve o timeout nativo 258 observado neste Windows usando `localhost`, inclusive com prefixo `tcp:`.

### 10.1. Persistência e cache

O bootstrap cria `BoilerplateE2E` se ausente e executa idempotentemente:

```sql
IF OBJECT_ID(N'dbo.Orders', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Orders (
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        Reference uniqueidentifier NOT NULL UNIQUE,
        Sku nvarchar(64) NOT NULL,
        Price decimal(18,2) NOT NULL,
        Currency char(3) NOT NULL
    );
END;
```

`POST /orders` recebe `{ "reference": "<GUID>", "sku": "SKU-E2E" }`, propaga `X-Correlation-Id` para o catálogo e insere preço/moeda obtidos da resposta externa. Não popula cache. `GET /orders/{id}` procura Redis; se ausente, lê SQL, responde o contrato público e grava o JSON em `e2e:orders:{id:D}`, com TTL de cinco minutos. Falha do catálogo retorna 502 antes da persistência. Não encontrar pedido retorna 404 e não cria chave de cache.

O cache é uma projeção do contrato com campos camelCase: `id`, `reference`, `sku`, `price`, `currency`. IDs são GUIDs, preço é decimal e moeda é BRL no mock. Nunca compare um campo de preço derivado da integração com o mesmo dado enviado pelo teste como se isso comprovasse o consumo do catálogo.

### 10.2. Mappings WireMock.Net

Monte a pasta versionada em `/app/__admin/mappings:ro` e inicie a imagem com `--Urls http://*:80 --ReadStaticMappings true`.

Mapping de sucesso:

```json
{
  "Guid": "da1dd227-0000-4000-8000-000000000001",
  "Request": {
    "Path": { "Matchers": [{ "Name": "ExactMatcher", "Pattern": "/catalog/products/SKU-E2E" }] },
    "Methods": ["GET"]
  },
  "Response": {
    "StatusCode": 200,
    "Headers": { "Content-Type": "application/json" },
    "BodyAsJson": { "sku": "SKU-E2E", "price": 42.50, "currency": "BRL" }
  }
}
```

Mapping de indisponibilidade:

```json
{
  "Guid": "da1dd227-0000-4000-8000-000000000002",
  "Request": {
    "Path": { "Matchers": [{ "Name": "ExactMatcher", "Pattern": "/catalog/products/SKU-UNAVAILABLE" }] },
    "Methods": ["GET"]
  },
  "Response": {
    "StatusCode": 503,
    "Headers": { "Content-Type": "application/json" },
    "BodyAsJson": { "code": "catalog_unavailable" }
  }
}
```

Quando o journal estiver habilitado, leia `GET /__admin/requests`, filtre `Request.Path` e o header `X-Correlation-Id` do GUID da jornada e anexe apenas a projeção com método, rota, GUID e contagem. O journal do mock pode conter requisições de outros testes; não o publique inteiro nem o apague globalmente em cenários paralelos.

### 10.3. Configuração e prontidão da API

A API recebe `CatalogBaseUrl=http://wiremock:80`, `ConnectionStrings__SqlServer` com banco/usuário/senha locais e `ConnectionStrings__Redis=redis:6379`. Os testes recebem endereços do host, não nomes internos do Compose. O runner não precisa de uma quarta chave raiz para o catálogo.

O Dockerfile deve publicar a API com SDK .NET 10, copiar o schema para a saída, restaurar com lock e executar sobre runtime ASP.NET Core 10 como usuário sem privilégios. Exclua `.git`, `node_modules`, `artifacts`, `bin`, `obj`, `.env` e configurações locais do contexto de build. A API do sample não registra requests, headers, bodies ou exceções de conexão em texto bruto.

SQL Server deve ter `ACCEPT_EULA=Y` e senha fornecida por ambiente; Redis deve responder `PING`; a API depende da saúde deles e do início do WireMock. O bootstrap da API tem 120 segundos para criar banco/schema e confirmar Redis e ambos os mocks. O script local usa `docker compose up -d --build --wait --wait-timeout 180` e depois tem prazo de 180 segundos para confirmar por HTTP preço/moeda, o mock 503 e `/health/ready` com `code=ready`. Cada tentativa tem timeout curto e intervalo controlado. Container em execução não é prova de mapping carregado.

O sample não declara volumes persistentes de SQL/Redis. `Down` remove containers e rede do projeto Compose, portanto seus dados deixam de existir. Em projeto real, ajuste o ciclo de vida à propriedade do ambiente; a suíte não deve desligar bancos compartilhados ou infraestrutura que não criou.

<a id="execucao"></a>

## 11. Entrada de execução, modos e artefatos

### 11.1. Modos e filtros atuais

| Modo de `scripts/e2e.ps1` | Filtro | Resultado esperado |
|---|---|---|
| `Standard` | `Category!=Diagnostic` | Nove testes aprovados na linha de base. |
| `Proof` | `Category=Proof` | Um teste async com anexo, sem stack. |
| `Configuration` | `Category=Configuration` | Cinco testes, sem infraestrutura externa. |
| `Http` | `Category=Http` | Um teste HTTP, sem clientes de observação SQL/Redis. |
| `Diagnostic` | `Category=Diagnostic` | Uma falha intencional específica após HTTP bem-sucedido. |

`-Sample` carrega a senha local e preenche endpoints do host e journal, sem imprimir os valores. O helper lê linhas `SQLSERVER_SA_PASSWORD=...` como dados e nunca executa o conteúdo do `.env`. Ambiente já definido prevalece sobre o arquivo. Para a interpolação da connection string no Compose, use senha forte com letras, números e caracteres como `!` ou `_`, evitando `;`, aspas e `$` nesse sample.

### 11.2. Estrutura de uma execução

```text
artifacts/20261114T144927136Z-29eaad37/
  adapter.json            # configuração efetiva do adaptador, sem credenciais
  runner.log
  tests.trx
  generator.log
  execution.json
  results/
    <id>-result.json
    <id>-attachment.json
    <id>-attachment.sql
    ...
  report/
    index.html
    data/test-results/
    data/attachments/
    widgets/tree.json
    ...
```

Use UTC com precisão de milissegundos mais sufixo aleatório no `run-id`. Nunca reutilize `results/` para uma rodada nova. O timestamp não é label do cenário. Se houver shards, cada um produz resultados próprios dentro da mesma identidade de execução; agregue somente resultados/anexos dessa execução. Não renomeie anexos nem copie um diretório antigo inteiro por conveniência. Histórico, se adotado, deve ter outra localização.

### 11.3. Algoritmo obrigatório do script compartilhado

1. Resolver a raiz a partir de `$PSScriptRoot`; salvar valores anteriores de `ALLURE_CONFIG` e `E2E_DIAGNOSTIC`.
2. Opcionalmente aplicar o helper do sample.
3. Gerar `run-id`; criar `artifacts/<id>/results` vazio.
4. Escrever `adapter.json` com diretório absoluto de resultados, `failExceptions` e reporter conforme seção 4.3.
5. Definir `ALLURE_CONFIG` com esse caminho; definir `E2E_DIAGNOSTIC=1` somente no modo Diagnostic.
6. Executar `dotnet test` no projeto selecionado, com `--settings e2e.runsettings`, filtro do modo, logger TRX e diretório da rodada. Capturar stdout/stderr em `runner.log`.
7. Guardar imediatamente o exit code do runner, antes de qualquer comando nativo posterior.
8. Procurar `*-result.json` apenas no diretório novo. Se existirem, tentar gerar relatório mesmo se o runner retornou erro.
9. Executar `node node_modules/allure/cli.js generate <results> --config allurerc.mjs --output <report>`, capturar `generator.log` e guardar o código do gerador.
10. Verificar integridade, agrupamentos, evidências, scripts SQL e sanitização; guardar seu código separadamente.
11. Gravar `execution.json`; imprimir caminho do relatório e devolver o código final com a precedência abaixo.
12. Em `finally`, restaurar variáveis temporárias e diretório do processo.

O script precisa tratar códigos nativos explicitamente. Em PowerShell 7, não deixe a preferência de erro de comandos nativos interromper o fluxo antes da geração após falha. Em Windows PowerShell, uma linha de stderr pode virar `NativeCommandError`; ela não autoriza descartar o exit code real nem suprimir a geração.

### 11.4. Manifesto e precedência de códigos

```json
{
  "runId": "20261114T144927136Z-29eaad37",
  "mode": "Standard",
  "runnerExit": 0,
  "generatorExit": 0,
  "validationExit": 0,
  "resultCount": 9,
  "report": "<caminho absoluto da rodada>/report",
  "exitCode": 0
}
```

| Condição | Código final |
|---|---|
| Runner falhou | Preservar seu código, inclusive no modo Diagnostic. |
| Runner passou, mas não há resultado novo | 3 |
| Runner passou, mas o gerador falhou | 4 |
| Runner passou, mas a verificação dos artefatos falhou | 5 |
| Tudo passou | 0 |

Quando o gerador/verificador não puder ser iniciado por ausência de resultados, registre um marcador como `-1`, não um sucesso fictício. Erros anteriores à invocação do runner, como pré-requisito ausente ou configuração de infraestrutura inválida, precisam produzir erro acionável; não os apresente como testes aprovados.

<a id="comandos"></a>

## 12. Comandos completos de operação

### 12.1. Preparar e executar o sample no Windows

Execute na raiz do checkout com .NET 10, Node 24 LTS, npm, Docker/Compose e containers Linux disponíveis:

```powershell
Copy-Item .env.example .env
# Edite .env: SQLSERVER_SA_PASSWORD=<senha local forte conforme seção 11.1>.
notepad .env
npm.cmd ci
dotnet restore Boilerplates.sln --locked-mode
dotnet build Boilerplates.sln --no-restore

# Comprove primeiro reporter, async e anexo, sem depender de infraestrutura.
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/e2e.ps1 -Mode Proof
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/e2e.ps1 -Mode Configuration

# Suba e confirme a stack, depois execute a jornada.
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/stack.ps1 Up
$env:E2E_SYNTHETIC_SECRET = 'synthetic-e2e-canary-719f28'
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/e2e.ps1 -Sample
$standardExit = $LASTEXITCODE
Write-Host "Standard: $standardExit" # esperado: 0

# Rodada independente: esperado 1 no runner, 0 no gerador/verificador.
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/e2e.ps1 -Sample -Mode Diagnostic
$diagnosticExit = $LASTEXITCODE
Write-Host "Diagnostic: $diagnosticExit" # esperado: 1

# Nova rodada padrão para comprovar isolamento e ausência de resultados herdados.
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/e2e.ps1 -Sample
```

Se estiver iniciando um projeto do zero, os arquivos de lock ainda não existem. Faça a primeira restauração/instalação para gerá-los, versione-os e somente então adote `--locked-mode` e `npm ci`. Se estiver migrando, use a solução/projeto reais e atualize os caminhos do script.

### 12.2. Conferir mocks e prontidão diretamente

```powershell
Invoke-RestMethod http://localhost:9090/catalog/products/SKU-E2E
# Esperado: sku=SKU-E2E; price=42.50; currency=BRL.
try {
    Invoke-WebRequest http://localhost:9090/catalog/products/SKU-UNAVAILABLE -UseBasicParsing
} catch { [int]$_.Exception.Response.StatusCode } # esperado: 503
Invoke-RestMethod http://localhost:5080/health/ready # esperado: code=ready
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/stack.ps1 Check
```

### 12.3. Executar contra uma API real

```powershell
$env:E2E_BASEURL = 'https://api.example.test/'
$env:E2E_SQLSERVER_CONNECTIONSTRING = '<valor fornecido pelo ambiente>'
$env:E2E_REDIS_CONNECTIONSTRING = '<valor fornecido pelo ambiente>'
# Não usar -Sample: ele pertence ao ambiente demonstrativo.
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/e2e.ps1
```

O exemplo de URL é fictício. O caminho, autenticação, dados e cenários devem ter sido adaptados ao contrato da API real. Em Bash, os overrides são exportados como `export E2E_BASEURL='http://localhost:5080'`; nunca use `echo` para registrar connection strings. Em PowerShell 7 no Linux, execute `pwsh -NoProfile -File scripts/e2e.ps1`; o CI entregue usa esse shell.

Para HTTP sem clientes SQL/Redis configurados no runner, execute em um novo processo sem overrides dessas conexões e sem JSON local que as preencha:

```powershell
$env:E2E_BASEURL = 'http://localhost:5080'
Remove-Item Env:E2E_SQLSERVER_CONNECTIONSTRING -ErrorAction SilentlyContinue
Remove-Item Env:E2E_REDIS_CONNECTIONSTRING -ErrorAction SilentlyContinue
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/e2e.ps1 -Mode Http
```

O sample ainda exige SQL/Redis na configuração da própria API. O comando comprova independência dos clientes de observação do runner, não independência arquitetural da aplicação.

### 12.4. Abrir, regenerar e encerrar

```powershell
$run = Get-ChildItem artifacts -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName 'execution.json') } |
    Sort-Object Name -Descending | Select-Object -First 1

Get-Content (Join-Path $run.FullName 'execution.json')
node node_modules/allure/cli.js open (Join-Path $run.FullName 'report')
# Ctrl+C encerra o servidor do relatório.

node node_modules/allure/cli.js generate (Join-Path $run.FullName 'results') `
    --config allurerc.mjs --output (Join-Path $run.FullName 'report-regenerated')

powershell -NoProfile -ExecutionPolicy Bypass -File scripts/stack.ps1 Down
```

O diretório mais recente pode ser de diagnóstico ou teste negativo; confira `mode` e códigos no manifesto antes de classificá-lo como rodada padrão. `-ExecutionPolicy Bypass` nos exemplos vale apenas para o processo iniciado. O relatório estático permanece após encerrar a stack.

<a id="diagnostico"></a>

## 13. Diagnóstico intencional da integração com Allure

Implemente um teste exclusivo de categoria `Diagnostic`, ignorado por padrão tanto pelo filtro quanto por atributo de descoberta condicionado a `E2E_DIAGNOSTIC=1`:

```csharp
public sealed class DiagnosticFactAttribute : FactAttribute
{
    public DiagnosticFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("E2E_DIAGNOSTIC") != "1")
            Skip = "Opt-in: use o modo Diagnostic do script E2E.";
    }
}
```

O teste usa os labels `Plataforma / Relatórios / Localizar asserção intencional após evidência HTTP`, abre o step `Diagnóstico: falhar asserção após capturar HTTP`, chama `/health/ready`, captura a evidência, exige status 200 e então executa:

```csharp
Assert.Fail("E2E_DIAGNOSTIC_EXPECTED: asserção intencional após evidência HTTP.");
```

Aceite do diagnóstico: exatamente um resultado, status `failed`, marcador acima na mensagem, step esperado com status `failed`, evidência HTTP no próprio step, runner retornando 1 e gerador/verificador retornando 0. Se a conexão HTTP falhar antes da asserção ou o adaptador classificar o teste como `broken`, o diagnóstico **não cumpriu seu objetivo**, mesmo que o processo tenha retornado erro.

<a id="verificador"></a>

## 14. Verificação automatizada dos artefatos

O verificador faz parte da entrega, não é uma inspeção opcional do HTML. Implemente-o como script Node com bibliotecas nativas (`fs`, `path`, `assert`), recebendo diretório da rodada e modo.

### 14.1. Resultados e anexos brutos

1. Listar `results/*-result.json`; exigir pelo menos um e parsear todos.
2. Percorrer `steps` recursivamente. Para cada item de `attachments`, verificar existência de `source` no diretório de resultados.
3. Para MIME JSON, fazer parse do anexo e exigir documento válido. Para SQL, verificar o contrato da seção 9.
4. Exigir anexos de step. Não aceitar apenas arquivos soltos que nenhum resultado referencia.
5. No Standard atual, exigir os dois cenários do domínio Pedidos. Para cada jornada aprovada, exigir steps com evidência HTTP/API, SQL Server e Redis. Uma jornada interrompida por falha pode não ter alcançado os steps posteriores; preserve o erro real do runner.

### 14.2. Consulta SQL e replay

Para cada anexo chamado `Comando SQL Server`, verificar:

- Template exato conhecido para `FindOrderById` ou `DeleteOwnedOrder`.
- Estado `prepared`, tipo Text, parâmetro `@id` único, tipo `uniqueidentifier` e GUID válido.
- Declaração do GUID e texto do comando no `replaySql` sem substituição aproximada.
- Prefixo de somente leitura ou de alteração de dados coerente com a operação.
- Anexo `text/plain` de reprodução no mesmo step e conteúdo idêntico ao `replaySql` do JSON.
- Na suíte padrão com ambas as jornadas aprovadas, quatro comandos SQL: duas observações e duas limpezas. Atualizar esse contrato ao adicionar jornadas.

Não execute os arquivos SQL nesse verificador. A inspeção comprova consistência dos artefatos; uma execução manual controlada de um replay de leitura é uma validação adicional distinta.

### 14.3. Renderização e hierarquia efetivamente carregadas

Exigir `report/index.html`, parsear `report/data/test-results/*.json` e comparar a quantidade de resultados com os resultados brutos da rodada. Localizar cada teste por `fullName`.

Na combinação 3.33.0 exercitada, steps renderizados têm `steps` que também podem conter itens `type="attachment"`. O campo `link.originalFileName` identifica o `source` do anexo bruto; `link.id` e `link.ext` apontam para `report/data/attachments/<id><ext>`. Para cada anexo bruto, exigir item correspondente dentro do mesmo step renderizado, `link.missed=false` e arquivo físico presente.

Parsear `report/widgets/tree.json`. Para cada teste, procurar os valores dos labels `epic`, `feature` e `story` e percorrer os grupos nessa ordem em `root.groups` e `groupsById`. Isso comprova a configuração BDD efetiva; somente encontrar labels no JSON bruto não comprova que o gerador carregou o agrupamento.

Esses caminhos e estruturas internas do relatório foram exercitados com a versão fixada. Ao atualizar o gerador, verificar novamente sua saída; não presumir compatibilidade desse formato interno.

### 14.4. Status, segredos e limite da verificação

Aplicar o contrato específico de Diagnostic da seção 13. Em outros modos, não transformar a existência de testes failed/broken em falha do gerador: o runner já decide o status e ele precisa ser preservado. Ainda assim, validar a integridade do relatório produzido no caminho de erro.

Percorrer todos os arquivos da rodada e procurar os segredos sintéticos/configurados conforme seção 8.4. Produzir erro com nome seguro do artefato, sem repetir o segredo encontrado. Não anexar um diff que revele o valor proibido.

A verificação dos dados renderizados e arquivos não substitui uma inspeção visual quando a mudança afeta layout, legibilidade ou preview de novo tipo de anexo. Registre separadamente o que foi verificado por script e o que foi aberto em navegador; não afirmar inspeção visual sem tê-la realizado.

<a id="ci"></a>

## 15. CI com o mesmo padrão local

A esteira deve instalar SDK/Node fixados, restaurar dependências pelo lock, preparar um ambiente isolado, executar o mesmo script e publicar artefatos mesmo após falha. A geração após erro pertence ao script compartilhado; não duplique uma segunda implementação de geração no YAML.

Exemplo equivalente ao workflow do boilerplate:

```yaml
name: API E2E
on: [push, pull_request, workflow_dispatch]
jobs:
  e2e:
    runs-on: ubuntu-latest
    timeout-minutes: 20
    env:
      SQLSERVER_SA_PASSWORD: ${{ secrets.E2E_SQLSERVER_SA_PASSWORD }}
      E2E_SYNTHETIC_SECRET: synthetic-ci-evidence-canary-719f28
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - uses: actions/setup-node@v4
        with: { node-version: '24', cache: npm }
      - run: npm ci
      - name: Gerar senha efêmera se não houver secret do repositório
        shell: pwsh
        run: |
          if (-not $env:SQLSERVER_SA_PASSWORD) {
            $password = 'E2e!' + [guid]::NewGuid().ToString('N')
            Write-Output "::add-mask::$password"
            "SQLSERVER_SA_PASSWORD=$password" >> $env:GITHUB_ENV
          }
      - name: Subir stack e confirmar mocks e prontidão
        shell: pwsh
        run: ./scripts/stack.ps1 Up
      - name: Testar, gerar e verificar o relatório
        shell: pwsh
        run: ./scripts/e2e.ps1 -Sample
      - name: Publicar artefatos da execução
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: api-e2e-${{ github.run_id }}-${{ github.run_attempt }}
          path: artifacts/
          if-no-files-found: error
          retention-days: 7
      - name: Encerrar somente a infraestrutura criada pelo job
        if: always()
        shell: pwsh
        run: ./scripts/stack.ps1 Down
```

Ao migrar para infraestrutura real, substitua os passos Up/Down pela preparação que o job possui e pelos secrets correspondentes; não desligue dependências compartilhadas. Em shards, inclua a identidade do shard no diretório/artefato e agregue somente shards da mesma execução/attempt. Uma falha de publicação deve aparecer como falha da etapa de upload, distinta dos códigos do runner e gerador no manifesto.

Não habilite `continue-on-error` no teste como forma de deixar o pipeline verde. O upload usa `always()` para preservar a evidência, não para apagar a falha. Se a infraestrutura falhar antes de gerar resultados, a ausência de artefatos precisa ficar visível.

<a id="migracao"></a>

## 16. Migração para um projeto real, em ordem

1. **Inventariar a solução.** Identificar SDK, targets, gerenciamento de pacotes, runner real do IDE/CI e pipeline existente. Registrar a combinação a preservar ou a alteração necessária.
2. **Criar a estrutura.** Adicionar o projeto E2E à solução; separar Configuration, Fixtures, Infrastructure, Reporting, Scenarios e Diagnostics conforme seção 2.
3. **Provar o adaptador antes do negócio.** Um `[Fact]` com labels estáveis, `await AllureApi.Step`, anexo JSON e asserção; executar pelo runner real, gerar HTML e verificar anexo no step renderizado. Só continuar após comprovar isso.
4. **Implementar configuração.** Usar as três chaves, precedência e mensagens seguras; comprovar carga no diretório de saída e overrides de ambiente. Respeitar o gerenciamento central de pacotes existente.
5. **Implementar a fixture.** HTTP reutilizado, SQL por operação, Redis sob demanda, descarte e cancelamento conforme seção 6. Não exigir todos os componentes para todos os testes.
6. **Definir um cenário representativo.** Escolher uma ação pública com efeito observável e um erro de integração relevante. Escrever o contrato da matriz antes de escrever asserções.
7. **Adaptar os pontos dependentes do domínio.** Rotas, DTOs, autenticação, tabelas, nomes/tipos dos parâmetros, SQL aprovado, chaves/TTL Redis, mocks e consistência. Não manter SKU, preço ou igualdade id/reference do sample sem correspondência real.
8. **Projetar evidências e SQL reproduzível.** Aprovar campos e valores por origem, queries e scripts, limites e política de dados sensíveis. Atualizar a sanitização junto com o novo contrato.
9. **Implementar isolamento e limpeza.** Referência exclusiva, inventário de recursos e consultas/exclusões delimitadas. Confirmar que uma falha após criação ainda permite localizar os dados próprios.
10. **Criar a entrada compartilhada e o verificador.** Atualizar caminho do `.csproj`, filtros, contagens e labels esperados. O verificador atual tem expectativas específicas para duas jornadas de Pedidos e quatro comandos SQL; adaptar esses pontos explicitamente.
11. **Exercitar falha e sanitização.** Diagnóstico intencional, ausência de configuração obrigatória, HTTP sem SQL/Redis e segredo sintético atravessando captura/artefatos. Não aceitar somente um happy path verde.
12. **Executar duas rodadas independentes.** Comparar diretórios, IDs e limpeza; gerar relatório e confirmar agrupamentos, `.sql` e demais anexos. Registrar comandos, códigos e caminhos efetivos.
13. **Integrar ao CI e exercitar publicação.** Mesmo script/configuração; relatório após falha; upload com always; preservar erro do runner. Se não houver acesso ao CI, registrar esse limite sem declarar publicação validada.
14. **Atualizar este guia no projeto de destino.** Substituir nomes/contratos fictícios e registrar versões e provas efetivas. Manter regras, comandos e limites suficientes para a próxima sessão repetir a validação.

### 16.1. Inventário de adaptações obrigatórias

| Ponto do sample | Decisão no projeto real |
|---|---|
| `Boilerplates.sln` e `Boilerplate.E2E.csproj` | Solução, nome, namespace e caminhos reais dos scripts. |
| Domínio Pedidos / feature do catálogo | Vocabulário e comportamentos reais, com labels estáveis. |
| `/orders` e `/health/ready` | Rotas públicas, eventual prefixo de base e contrato de prontidão. |
| Bearer sintético | Autenticação real separada do teste de sanitização. |
| `dbo.Orders` e GUID `@id` | Queries/params aprovados, replay seguro e autorização de leitura/limpeza. |
| `e2e:orders:{id:D}`, TTL 300 | Convenção de chaves, propriedade e prazo real do cache. |
| `id=reference` | Forma de correlacionar referência, id e efeitos sem assumir igualdade. |
| Catálogo WireMock local | Dependência simulada apropriada ao ambiente e seu contrato de falha. |
| Retorno 502/ausência de efeitos | Contrato de erro e garantias reais da aplicação. |
| Suíte com nove testes | Novos testes, filtros, cobertura e contagens atualizadas no verificador. |
| Quatro comandos SQL no Standard | Cobertura de evidências das operações SQL efetivamente adicionadas. |
| Up/Down do Compose | Propriedade e ciclo de vida da infraestrutura usada pela suíte. |

<a id="problemas"></a>

## 17. Diagnóstico de problemas recorrentes

| Sintoma | O que verificar sem expor segredos |
|---|---|
| Runner encontra testes, mas não há resultado Allure | Pacote/adaptador, runner 2.8.2, ReporterSwitch, cópia dos JSONs e caminho efetivo de ALLURE_CONFIG. |
| Runner retorna sucesso com zero resultados novos | Diretório exclusivo, filtro, descoberta e reporter; devolver erro de ausência de resultados. |
| O relatório mostra suite em vez de domínio/feature/cenário | `--config` explícito, configuração Awesome e árvore efetivamente gerada em widgets/tree.json. |
| Anexo está na pasta, mas não aparece no step | Referência `source`, step ativo, await da operação e vínculo no JSON renderizado. |
| SQL retorna timeout 258 no Windows com localhost | Endereço/porta publicada; usar o loopback IPv4 explicitamente exercitado, sem imprimir a string de conexão. |
| Há JSON de comando SQL, mas não resultado completed | Comando preparado sem conclusão confirmada; verificar falha da operação e não presumir ausência de efeitos. |
| Replay SELECT retorna zero após teste aprovado | Confirmar id/banco/schema e a limpeza automática; comparar com o snapshot observado no instante do teste. |
| Replay exige um segredo | Artefato deve ter placeholder e reproducible=false; preencher localmente, nunca publicar o valor. |
| Chave Redis existe, mas teste deveria provar cache-aside | Conferir ausência antes da leitura pública, isolamento do id e comparação do conteúdo após a leitura. |
| Diagnóstico falha por HTTP em vez da asserção esperada | Corrigir prontidão/configuração; a prova do diagnóstico continua pendente. |
| Limpeza mascara a asserção | Guardar estado da falha original, tentar todos os recursos e separar o resultado de limpeza. |
| Testes passam isolados, falham em paralelo | Colisões de referência/chave, estado mutável compartilhado ou reset global de mock/cache. |
| Job verde depois de teste com erro | Verificar captura imediata do exit code, precedência final e uso indevido de continue-on-error. |
| Gerador falhou, mas há HTML antigo | Conferir diretório novo e manifesto; não publicar saída herdada como relatório da rodada. |

<a id="aceite"></a>

## 18. Critérios de aceite e registro da próxima sessão

A pessoa ou agente que implementar ou migrar o padrão deve preencher o registro abaixo com fatos da execução. Não considerar uma mudança concluída apenas porque compilou.

| Verificação | Aceite |
|---|---|
| Build e locks | Restore reproduzível; build com códigos registrados; avisos relevantes tratados ou descritos. |
| Prova do adaptador | Teste descoberto pelo runner real, async aguardado, anexo vinculado e renderização confirmada. |
| Configuração | JSON encontrado na saída; local e ambiente têm a precedência prevista; ausências geram mensagens seguras. |
| Independência HTTP | Cenário HTTP aprovado sem configurar clientes SQL/Redis do runner. |
| Jornada principal | Ação HTTP, integração externa relevante, SQL e Redis observados conforme o contrato. |
| Erro de integração | Cenário de erro passa ao observar resposta/ausência de efeitos pactuadas. |
| SQL exato | Texto corresponde ao comando real, parâmetros tipados correspondem aos valores seguros usados e replay .sql está no mesmo step. |
| SQL seguro | Limpeza identificada como alteração; valores sensíveis removidos; prepared/completed não são confundidos. |
| Evidências | Capturadas antes dos asserts, com MIME correto, volume limitado e referências válidas nos resultados/renderização. |
| Diagnóstico intencional | Runner falha pela asserção específica; relatório/verificador passam; evidência no step correto. |
| Isolamento | Duas rodadas com diretórios/IDs próprios; sem dependência de ordem ou resultados antigos. |
| Limpeza | Somente recursos próprios; erros secundários não substituem a falha original. |
| Sanitização | Canário percorreu captura; ausente dos artefatos relevantes; campos permitidos continuam legíveis. |
| CI | Mesmo script; geração após falha; status preservado; publicação validada ou limitação explicitamente registrada. |
| Entrega | Guia atualizado, comandos e exit codes registrados, caminho do relatório e estado da infraestrutura informados. |

Modelo de registro para uma nova sessão:

```text
Data e ambiente:
SDK / framework / xUnit / runner / adaptador / gerador / Node:
Domínio → feature → cenários:
Componentes observados e justificativa:
Arquivos criados/alterados:
Comandos efetivamente executados e seus exit codes:
Run-id e caminho do relatório padrão:
Resultado do diagnóstico intencional (runner / gerador / verificador):
Queries e scripts SQL conferidos; eventual replay de leitura executado:
Prova de sanitização e representações inspecionadas:
Prova de isolamento e resultado da limpeza:
Publicação CI efetivamente exercitada ou motivo da pendência:
Estado final da infraestrutura:
Limitações observadas e próximo passo concreto, se houver:
```

Na linha de base original, nove testes passaram, os agrupamentos e anexos foram verificados pelos arquivos renderizados, o diagnóstico retornou 1 com gerador/verificador 0 e a stack foi encerrada. A extensão com SQL reproduzível foi exercitada em `20261114T152138718Z-e4aeecc3`: nove testes aprovados, 32 anexos de step, quatro comandos SQL com JSON e `.sql`, runner/gerador/verificador com código 0. Um SELECT aprovado extraído do anexo também foi executado diretamente no SQL Server com código 0 e zero linhas após a limpeza automática, como previsto. O relatório dessa prova está em `artifacts/20261114T152138718Z-e4aeecc3/report`; o diretório é gerado e não versionado. Novas sessões devem registrar sua própria execução, sem reutilizar contagens históricas como prova.

A publicação hospedada em GitHub Actions não havia sido exercitada por ausência de remote configurado. A renderização foi inspecionada por seus dados e arquivos, sem inspeção visual em navegador. Esses limites permanecem explícitos, sem impedir a reprodução local especificada neste guia.
