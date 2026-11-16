# E2E de APIs .NET com xUnit e Allure Report 3

Boilerplate executável com três pontos de acesso independentes: HTTP para a ação de negócio, SQL Server para observar persistência e Redis para observar cache. O sample contém quatro serviços Docker Compose, incluindo um catálogo simulado com WireMock.Net.

O [guia E2E autossuficiente](docs/E2E-GUIDE.md) especifica o padrão completo para novas sessões e migração: domínio → feature → cenário, responsabilidades, evidências, SQL reproduzível, isolamento, execução, CI e critérios de aceite.

## Iniciar, testar e encerrar no Windows

Pré-requisitos: SDK .NET 10, Node.js 24 LTS, npm, Docker com containers Linux e Docker Compose. PowerShell 5.1 ou 7. Reserve memória para SQL Server (pelo menos 2 GB para ele) e as portas abaixo. Não é necessário Java.

Execute na raiz do checkout:

```powershell
Copy-Item .env.example .env
# Edite .env e substitua SQLSERVER_SA_PASSWORD por uma senha local forte.
# Use letras, números e ! ou _, sem ;, aspas ou $, para a interpolação do Compose.
notepad .env
npm.cmd ci
dotnet restore Boilerplates.sln --locked-mode
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/stack.ps1 Up
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/e2e.ps1 -Sample
$testExit = $LASTEXITCODE
Write-Host "Resultado da suíte: $testExit"
```

`stack.ps1 Up` constrói somente a imagem da API, aguarda a saúde de SQL Server/Redis e verifica por HTTP os dois mappings e `/health/ready`, com prazo de 180 segundos após o Compose. O bootstrap da API tem prazo próprio de 120 segundos: cria o banco se necessário, aplica `init.sql` idempotente e confirma SQL, Redis e respostas do catálogo. `Check` repete somente a verificação de prontidão.

Cada execução imprime um diretório `artifacts/<UTC>-<id>/`. Para abrir o relatório mais recente:

```powershell
$run = Get-ChildItem artifacts -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName 'execution.json') } |
    Sort-Object Name -Descending | Select-Object -First 1
node node_modules/allure/cli.js open (Join-Path $run.FullName 'report')
# Ctrl+C encerra o servidor do relatório.
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/stack.ps1 Down
```

`Down` encerra os quatro serviços e a rede deste projeto Compose. O sample não declara volumes persistentes de banco/cache. Recriar os containers reinicia seus dados. `.env`, configurações locais, resultados e relatórios são ignorados pelo Git e excluídos do contexto de build Docker.

## Endereços e contrato do sample

| Serviço | Endereço interno do Compose | Runner no host |
|---|---|---|
| API | `http://api:8080` | `http://localhost:5080` |
| SQL Server | `sqlserver,1433` | `tcp:127.0.0.1,14333` |
| Redis | `redis:6379` | `localhost:16379` |
| WireMock.Net | `http://wiremock:80` | `http://localhost:9090` |

As portas são publicadas apenas em `127.0.0.1`. SQL usa o endereço IPv4 explícito de localhost: a validação neste Windows apresentou timeout nativo `258` com `localhost`, inclusive com o prefixo `tcp:`, e passou com `tcp:127.0.0.1`. A URL externa pertence à configuração `CatalogBaseUrl` da **API**, não às três chaves dos testes. `E2E_WIREMOCK_ADMIN_URL` habilita apenas uma observação opcional do journal; `-Sample` configura essa observação automaticamente.

Os mappings são montados em `/app/__admin/mappings`, com `--ReadStaticMappings true`. Imagens exigidas: `mcr.microsoft.com/mssql/server:2022-latest`, `redis:7-alpine` e `sheyenrath/wiremock.net:latest`. Essas tags são mutáveis por requisito; o [registro da validação](docs/validation/allure-e2e.md) registra as versões efetivamente exercitadas.

```powershell
Invoke-RestMethod http://localhost:9090/catalog/products/SKU-E2E
# sku=SKU-E2E, price=42.50, currency=BRL
try {
    Invoke-WebRequest http://localhost:9090/catalog/products/SKU-UNAVAILABLE -UseBasicParsing
} catch { [int]$_.Exception.Response.StatusCode } # 503
Invoke-RestMethod http://localhost:5080/health/ready # code=ready
```

Jornada de sucesso: `POST /orders` recebe `{ reference: <GUID exclusivo>, sku: "SKU-E2E" }`, chama o catálogo propagando `X-Correlation-Id` e persiste preço/moeda da resposta. Retorna 201 com `id`, `reference`, `sku`, `price`, `currency`; neste sample `id = reference`. A consulta SQL parametrizada usa o identificador retornado. O cache deve estar vazio antes de `GET /orders/{id}`; a leitura retorna 200 e popula `e2e:orders:{id:D}` por cinco minutos. O teste compara os campos públicos e o valor em cache e exige `0 < TTL <= 300` segundos.

Jornada de indisponibilidade: o SKU `SKU-UNAVAILABLE` recebe 503 do catálogo; a API responde **502**, com `code=catalog_unavailable`. Não cria pedido nem cache. O teste verifica SQL vazio, consulta pública 404 e chave ausente. Ambos os cenários confirmam exatamente uma chamada externa com sua correlação quando o journal está habilitado.

A limpeza usa o GUID da própria jornada, `DELETE ... WHERE Id=@id AND Reference=@id` e `DEL` de uma única chave. Tenta ambos os recursos com prazo independente de 15 segundos. Se já houver falha, registra erros de limpeza sem substituir a exceção original. Não há `FLUSHDB`, exclusão global de registros ou reset global do journal. Classes podem executar em paralelo; cada classe recebe sua fixture e cada jornada tem seu GUID. O xUnit v2 serializa os métodos da mesma classe.

## Configuração dos testes

`tests/Boilerplate.E2E/appsettings.json` contém exatamente estas chaves raiz:

```json
{
  "baseurl": "http://localhost:5080",
  "sqlserver-connectionstring": "",
  "redis-connectionstring": ""
}
```

O carregamento usa `AppContext.BaseDirectory`, com cópia dos arquivos pelo projeto, independente do diretório do runner ou IDE. O mapeamento de cada chave com hífen é explícito em `E2eSettings`.

Precedência: **appsettings.json < appsettings.local.json < variáveis de ambiente**. Uma variável definida como vazia também sobrescreve o JSON. O arquivo local é opcional e ignorado pelo Git; pode conter somente as chaves que deseja substituir.

| Chave | Override de ambiente |
|---|---|
| `baseurl` | `E2E_BASEURL` |
| `sqlserver-connectionstring` | `E2E_SQLSERVER_CONNECTIONSTRING` |
| `redis-connectionstring` | `E2E_REDIS_CONNECTIONSTRING` |

Em PowerShell, sem `-Sample`, para outra API compatível:

```powershell
$env:E2E_BASEURL = 'https://api.example.test/'
$env:E2E_SQLSERVER_CONNECTIONSTRING = '<connection string fornecida pelo ambiente>'
$env:E2E_REDIS_CONNECTIONSTRING = '<configuração fornecida pelo ambiente>'
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/e2e.ps1
```

No sample, `-Sample` lê somente `SQLSERVER_SA_PASSWORD` do `.env`, sem executar seu conteúdo. Ambiente já definido prevalece. O helper monta os endpoints do host e não imprime as connection strings. SQL Server e Redis são exigidos somente quando `Sql()` ou `RedisAsync()` são usados. A URL deve ser absoluta HTTP(S), sem credenciais, query ou fragmento. Erros de configuração indicam a chave e seu override; erros dos clientes omitem mensagem original e inner exception, incluindo somente tipo e códigos seguros.

Para comprovar um cenário HTTP sem configurar conexões SQL/Redis no runner:

```powershell
$env:E2E_BASEURL = 'http://localhost:5080'
$env:E2E_SQLSERVER_CONNECTIONSTRING = ''
$env:E2E_REDIS_CONNECTIONSTRING = ''
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/e2e.ps1 -Mode Http
```

A API do sample continua dependendo de sua própria stack; esse comando dispensa os **clientes de observação** SQL/Redis dos testes.

## Execução e relatórios

| Modo de `scripts/e2e.ps1` | Seleção |
|---|---|
| `Standard` (padrão) | 9 testes: duas jornadas, HTTP, prova do adaptador e cinco verificações de configuração/sanitização |
| `Proof` | Teste mínimo descoberto pelo runner com step assíncrono e anexo |
| `Configuration` | Cinco verificações sem infraestrutura externa |
| `Http` | Prontidão via HTTP, sem exigir connection strings |
| `Diagnostic` | Uma asserção intencional, depois de evidência HTTP; deve retornar 1 |

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/e2e.ps1 -Mode Proof
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/e2e.ps1 -Mode Configuration
$env:E2E_SYNTHETIC_SECRET = 'synthetic-e2e-canary-719f28'
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/e2e.ps1 -Sample
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/e2e.ps1 -Sample -Mode Diagnostic
# $LASTEXITCODE = 1; execution.json deve indicar runnerExit=1, generatorExit=0, validationExit=0.
```

O diagnóstico tem um atributo opt-in que também o ignora em uma execução direta pelo IDE quando `E2E_DIAGNOSTIC` não é `1`. O script padrão ainda o exclui pelo filtro `Category!=Diagnostic`. Não altere a suíte padrão para esperar falha desse teste.

Existem duas configurações separadas:

- **Adaptador .NET:** `allureConfig.json` copiado para a saída. O script cria `artifacts/<run>/adapter.json` com diretório absoluto exclusivo, selecionado por `ALLURE_CONFIG`.
- **Gerador Allure Report 3:** `allurerc.mjs`, passado explicitamente por `--config` tanto localmente quanto no CI. Usa Awesome, idioma `en` e agrupamento `epic → feature → story`. Neste boilerplate, esses níveis representam domínio, capacidade e comportamento. Severidade é aplicada somente no método; não depende de precedência entre classe e método. Não inventamos responsáveis ou links de tickets.

`e2e.runsettings` seleciona explicitamente `ReporterSwitch=allure`. Não atualize isoladamente `xunit.runner.visualstudio` para 3.x: o reporter do adaptador v2 depende do runner 2.x. A [documentação do adaptador](https://allurereport.org/docs/xunit/) descreve essa restrição. xUnit v3 com `Allure.Xunit.v3`/MTP é uma migração alternativa **não validada neste checkout**; nenhum padrão de MTP foi presumido.

Para regenerar um relatório de uma execução específica:

```powershell
node node_modules/allure/cli.js generate (Join-Path $run.FullName 'results') --config allurerc.mjs --output (Join-Path $run.FullName 'report-regenerated')
```

O script sempre tenta gerar o relatório quando há resultados, mesmo se o runner falhar. `execution.json` registra os códigos separados. A falha do runner prevalece no código final; com runner bem-sucedido, ausência de resultados retorna 3, falha do gerador 4 e falha da verificação de artefatos 5. Falhas de build/descoberta/reporter não são confundidas com sucesso por zero testes.

`verify-artifacts.mjs` verifica resultados novos, anexos JSON válidos, arquivos referenciados, correspondência entre steps brutos e renderizados, árvore BDD efetivamente gerada e ausência do segredo sintético/senha local em resultados, logs e relatório. No modo diagnóstico exige especificamente status `failed`, marcador `E2E_DIAGNOSTIC_EXPECTED` e anexo no step da asserção. A falha esperada do runner permanece 1.

Histórico, retries, flakiness, categorias customizadas, quality gate e HTML único não foram habilitados: não são necessários para esta demonstração. Os diretórios por execução evitam mistura de resultados. Se adicionar shards, reúna somente `results/` da mesma execução e preserve os nomes de anexos; mantenha histórico em localização separada. Não há promessa de desativação de telemetria: essa lacuna da pesquisa não foi convertida em configuração presumida.

## Contrato de evidências e limites

As operações anexam evidência **antes** da asserção, dentro de steps assíncronos nomeados. HTTP inclui método, rota sem query, status, correlação GUID e projeção de resposta; SQL inclui operação, GUID, quantidade de linhas e preço/moeda esperados/observados; Redis inclui operação, chave construída por GUID, presença, projeção e TTL. O journal publica apenas rota, método, GUID e contagem.

SQL também anexa o texto exato do comando aprovado, parâmetro `@id` tipado como `uniqueidentifier` e um `.sql` reproduzível com `DECLARE` e a consulta original. O comando é registrado como `prepared`; a observação/limpeza só recebe `completed` após o retorno. Scripts de leitura e de limpeza são identificados separadamente; a limpeza leva o aviso **ALTERA DADOS**. Somente templates estáticos revisados e GUIDs gerados pela jornada são publicados. Após a limpeza automática, repetir o SELECT pode retornar zero linhas: o anexo de observação conserva o resultado do instante do teste.

Não são serializados headers, cookies, query strings, SQL arbitrário, connection strings, payloads brutos ou mensagens originais de exceções. Campos de resposta são permitidos explicitamente: GUIDs, preço numérico limitado, SKUs e moedas conhecidos e códigos de erro conhecidos. Adapte a lista permitida ao novo contrato antes de anexar novos campos. JSON ilegível ou inesperado recebe uma descrição segura; corpos acima de 16 KiB são descartados; anexos acima de 8 KiB viram JSON válido com `truncated=true`. `Masked`/`Hidden` não são usados como proteção de segredos.

O teste adversarial passa um segredo sintético em query, header e resposta por `ApiClient`, verifica sua remoção antes da serialização e deixa o scanner conferir os artefatos. Nas jornadas reais, o mesmo segredo atravessa o header HTTP, que não é capturado. O scanner também confere a senha local quando disponível. Os resultados do adaptador contêm metadados automáticos como host, thread, linguagem e framework; não forneça segredos em nomes, atributos, parâmetros de `[Theory]` ou mensagens de asserção.

HTTP reutiliza `HttpClient` por fixture, com prazo de dez segundos para headers **e corpo** e cancelamento propagado. SQL abre/descarta conexão por operação, aproveita pooling, usa parâmetros tipados e timeout de cinco segundos. Redis reutiliza `ConnectionMultiplexer`, com conexão/operação de cinco segundos; `WaitAsync(token)` cancela a espera, **não retira um comando Redis já enviado**. As jornadas têm prazo de 45 segundos. A fixture encerra somente os recursos que criou.

## Estrutura e extensão

```text
Boilerplates.sln / global.json / Directory.Build.props
compose.yaml / .env.example / .dockerignore
allurerc.mjs / e2e.runsettings / package.json / package-lock.json
.github/workflows/e2e.yml
scripts/{stack,sample-env,e2e}.ps1
scripts/verify-artifacts.mjs
samples/ReferenceApi/{Program.cs,ReferenceApi.csproj,Dockerfile,appsettings.json,packages.lock.json}
samples/sqlserver/init.sql
samples/wiremock/mappings/catalog-product-{found,unavailable}.json
tests/Boilerplate.E2E/
  Boilerplate.E2E.csproj / packages.lock.json
  appsettings.json / allureConfig.json / xunit.runner.json
  Configuration/E2eSettings.cs
  Fixtures/E2eFixture.cs
  Infrastructure/Http/ApiClient.cs
  Infrastructure/SqlServer/SqlServerClient.cs
  Infrastructure/Redis/RedisClient.cs
  Reporting/{AllureEvidence,EvidenceSanitizer}.cs
  Scenarios/{ReferenceJourneyTests,HttpOnlyTests}.cs
  Diagnostics/{AdapterProofTests,ConfigurationTests,AllureFailureTests}.cs
docs/research/allure-e2e/              # pesquisa original preservada
docs/E2E-GUIDE.md                     # padrão completo, migração e reprodução
docs/validation/allure-e2e.md          # execução e limites efetivamente observados
```

Para adicionar um cenário HTTP, siga `HttpOnlyTests`: use `IClassFixture<E2eFixture>`, `[Fact]`, categoria, labels estáveis, `AllureApi.Step` e `fixture.Api.SendAsync`. Faça a asserção após a captura. Não chame `Sql()` nem `RedisAsync()` se o cenário não precisar deles. Ajuste `EvidenceSanitizer.Route/Project` para permitir somente o novo contrato.

Para uma jornada com observação SQL/Redis, siga `ReferenceJourneyTests.Isolated`: valide os componentes usados, crie GUID próprio, acione o negócio pela API, consulte SQL com método específico parametrizado, observe a chave Redis isolada e limpe ambos em `finally` preservando a falha original. Adicione asserções dos campos relevantes e evite passar objetos brutos a mensagens de falha. Atualize o verificador de artefatos se a quantidade de jornadas de referência mudar.

## CI e matriz de versões

O checkout original não tinha pipeline. `.github/workflows/e2e.yml` usa Ubuntu, Node 24 e PowerShell 7, com os mesmos scripts. A senha pode vir do secret `E2E_SQLSERVER_SA_PASSWORD`; sem ele, o job gera uma senha efêmera mascarada. Em Bash, o equivalente de configuração é `export E2E_BASEURL='http://localhost:5080'`; a esteira entregue usa `$env:NOME = 'valor'` em `pwsh`, como nos exemplos acima.

O upload usa `if: always()`, `if-no-files-found: error` e nome com run/attempt, preservando a falha do runner e sinalizando falha de publicação como etapa própria. A parada da stack também roda com `always()`. O caminho de geração após falha foi validado localmente com o diagnóstico.

| Componente | Versão validada |
|---|---|
| SDK / target | .NET SDK 10.0.404 / `net10.0` |
| xUnit / runner VSTest | 2.9.3 / `xunit.runner.visualstudio` 2.8.2 |
| Microsoft.NET.Test.Sdk | 17.14.1 |
| Adaptador / Commons | Allure.Xunit / Allure.Net.Commons 2.15.0 |
| Gerador | `allure` 3.33.0, lock npm versionado |
| SQL client | Microsoft.Data.SqlClient 6.1.8 |
| Redis client | StackExchange.Redis 2.9.32 |
| Node | 24.22.3 na validação final; host 25.9.0 também gerou a prova inicial, com aviso de engine em dependência |
| Shell local / CI | Windows PowerShell 5.1 exercitado / pwsh 7 configurado |
| Docker / Compose | Engine 29.9.2 / Compose 5.7.0 |

Os locks NuGet e npm são versionados; Docker restaura com `--locked-mode`. A instalação npm reportou seis avisos moderados em dependências de desenvolvimento do gerador; não foi aplicado upgrade incompatível automático. Os avisos iniciais de `System.Security.Cryptography.Pkcs` foram resolvidos pela atualização do SqlClient antes da validação final.


### Relatório Allure no GitHub Pages

O workflow também publica o relatório em https://correriadev.github.io/e2e-boilerplate/.
Na configuração inicial do repositório, selecione **Settings → Pages → Build and deployment → Source: GitHub Actions**.
Execute **Actions → API E2E → Run workflow**, na branch `main`, ou faça um push nessa branch.

- Somente execuções `push`/manuais de `main` publicam. Pull requests e outras branches mantêm apenas o artefato de diagnóstico.
- O site contém somente `report/`, não os logs, TRX, configuração do adaptador ou resultados brutos da pasta `artifacts/`.
- A publicação exige geração e validação das evidências bem-sucedidas, incluindo a checagem de ausência dos segredos conhecidos. Uma falha de teste não impede publicar um relatório válido e continua deixando a execução vermelha.
- Se não houver relatório validado, o último site publicado permanece disponível; confira a execução no Actions para não confundi-lo com um resultado novo.
- O artefato `api-e2e-<run>-<attempt>` continua disponível por sete dias. O site mostra o último relatório publicado, sem histórico entre execuções.

**O GitHub Pages deste repositório público é público.** Este workflow executa o sample com dados sintéticos. Não aponte a suíte para ambientes reais nem adicione dados pessoais, tokens ou corpos brutos às evidências sem revisar a sanitização e a política de publicação.
