# Validação executada — 2026-11-14

Checkout original: somente `docs/research/allure-e2e/`, sem solução, SDK fixado, projetos, gerenciamento de pacotes ou pipeline. A pesquisa original foi preservada. Referências operacionais confirmadas: [reporter xUnit](https://allurereport.org/docs/xunit/), [steps e anexos](https://allurereport.org/docs/xunit-reference/), [configuração Allure 3](https://allurereport.org/docs/v3/configure/) e [mappings WireMock.Net](https://wiremock.org/dotnet/mapping/).

## Ambiente efetivamente exercitado

- Windows x64, SDK 10.0.404, target `net10.0`, runtime host/container 10.0.15.
- xUnit 2.9.3, `xunit.runner.visualstudio` 2.8.2, Microsoft.NET.Test.Sdk 17.14.1.
- Allure.Xunit e Allure.Net.Commons 2.15.0; gerador npm `allure` 3.33.0.
- Microsoft.Data.SqlClient 6.1.8; StackExchange.Redis 2.9.32.
- Windows PowerShell 5.1; Docker Engine 29.9.2 e Compose 5.7.0.
- Node 25.9.0 na prova inicial; Node 24.22.3 nas rodadas finais. Node 24 foi obtido temporariamente com `npm.cmd exec --yes --package=node@24.22.3 -- node -p process.execPath`; o diretório retornado foi acrescentado ao `PATH` dos processos finais, sem alteração global do sistema.

Digests observados (as tags exigidas são mutáveis):

| Imagem | Digest SHA-256 |
|---|---|
| `mcr.microsoft.com/mssql/server:2022-latest` | `4525e3efac9f3e241cb003bb2ff2a82818b502f37e12049d788b1a9fa7e13b7f` |
| `redis:7-alpine` | `7f7d30b7fdd09467bac58198a1f166dd69789d348d4fe859cccb20e0cf287fbe` |
| `sheyenrath/wiremock.net:latest` | `5aa3d5376dea6070571429a62fc071486efa9892871d6df5181df7a97f66628c` |

## Comandos e códigos observados

Os comandos abaixo foram executados na raiz. Os scripts são iniciados com `powershell -NoProfile -ExecutionPolicy Bypass -File`; a política vale somente para esse processo. Para as rodadas com scanner foi definido `E2E_SYNTHETIC_SECRET=synthetic-e2e-canary-719f28`. A senha SQL foi gerada em `.env` ignorado, sem ser impressa.

| Comando efetivamente executado | Saída / código |
|---|---|
| `dotnet --info` / `node --version` / `docker version` / `docker compose version` | 0; versões acima |
| `npm.cmd view allure version` | 0; 3.33.0 |
| `dotnet new sln --name Boilerplates --format sln` e `dotnet sln ... add ...` | 0; solução com testes e API |
| `npm.cmd install` | 0; lock gerado; seis avisos moderados em dependências dev |
| Node 24.22.3 executando o `npm-cli.js ci` instalado | 0; reinstalação limpa de 158 pacotes a partir de `package-lock.json`, sem aviso de engine; seis avisos moderados dev |
| `dotnet test tests/Boilerplate.E2E --settings e2e.runsettings --filter Category=Proof` | 0; um teste aprovado com step assíncrono/anexo |
| `node_modules/.bin/allure.cmd generate tests/Boilerplate.E2E/bin/Debug/net10.0/allure-results --config allurerc.mjs --output artifacts/proof/report` | 0; HTML e anexo renderizado |
| `docker compose pull` após gerar `.env` | 0; três imagens exigidas |
| `powershell ... -File scripts/stack.ps1 Up` | 0; build, quatro serviços e prontidão/mappings confirmados |
| `Invoke-RestMethod http://localhost:9090/catalog/products/SKU-E2E` | 0; SKU-E2E, 42.50, BRL |
| Verificação de `/catalog/products/SKU-UNAVAILABLE` pelo script de stack | HTTP 503 esperado, tratado; script retorna 0 |
| `Invoke-RestMethod http://localhost:5080/health/ready` | 0; code=ready |
| `powershell ... -File scripts/e2e.ps1 -Mode Configuration` | 0; quatro testes na versão inicial; quinto teste incluído e aprovado nas rodadas padrão posteriores |
| `powershell ... -File scripts/e2e.ps1 -Sample` com Node 24 e IPv4 | 0; nove aprovados, 24 anexos de step |
| `powershell ... -File scripts/e2e.ps1 -Sample -Mode Diagnostic` | **1 esperado**; um failed por `E2E_DIAGNOSTIC_EXPECTED`; gerador 0, verificador 0 |
| Segunda execução `powershell ... -File scripts/e2e.ps1 -Sample` | 0; nove aprovados, 24 anexos, diretório e GUIDs independentes |
| `powershell ... -File scripts/e2e.ps1 -Mode Http`, com connection strings vazias | 0; um aprovado, sem inicializar clientes SQL/Redis |
| `powershell ... -File scripts/e2e.ps1`, sem `-Sample` e com connection strings vazias | **1 esperado**; duas jornadas rejeitadas antes de criar recursos, mensagem `Preencha sqlserver-connectionstring / E2E_SQLSERVER_CONNECTIONSTRING ...`; sete outros aprovados; gerador 0, verificador 0 |
| `dotnet restore Boilerplates.sln --locked-mode` | 0; locks consistentes |
| `dotnet build Boilerplates.sln --no-restore` | 0; zero avisos e zero erros |
| `git diff --check` | 0 |
| Rodada final `powershell ... -File scripts/e2e.ps1 -Sample`, após reinstalação limpa npm | 0; nove aprovados, 24 anexos, gerador 0 e verificador 0 |
| `powershell ... -File scripts/stack.ps1 Down` | 0; quatro containers e rede removidos; relatórios preservados |

Relatórios inspecionados por verificação automatizada de conteúdo:

| Finalidade | Diretório relativo à raiz | Runner / gerador / verificador |
|---|---|---|
| Prova inicial | `artifacts/proof/report` | 0 / 0 / inspeção de JSON e arquivos |
| Configuração inicial | `artifacts/20261114T143541603Z-b9e26393/report` | 0 / 0 / 0 |
| Jornada completa | `artifacts/20261114T144108862Z-12d7ffd9/report` | 0 / 0 / 0 |
| Diagnóstico intencional | `artifacts/20261114T144159655Z-cb76e637/report` | 1 / 0 / 0 |
| Segunda jornada completa | `artifacts/20261114T144243466Z-2d80af42/report` | 0 / 0 / 0 |
| HTTP sem conexões SQL/Redis | `artifacts/20261114T144515147Z-1b54e57f/report` | 0 / 0 / 0 |
| Configuração SQL ausente | `artifacts/20261114T144601240Z-9e5b982f/report` | 1 / 0 / 0 |
| Rodada final após instalação limpa | `artifacts/20261114T144927136Z-29eaad37/report` | 0 / 0 / 0 |

Os diretórios são ignorados pelo Git. Cada rodada do script tem `execution.json`, TRX, logs, configuração efetiva do adaptador, resultados brutos, anexos e relatório HTML. Não há resultados antigos nos diretórios novos.

## Evidência dos critérios de aceite

- A prova inicial precedeu a implementação das jornadas e confirmou a descoberta do reporter pelo runner 2.8.2. O resultado bruto apontava para anexo JSON dentro de step assíncrono, e o gerador 3.33.0 produziu os dados renderizados correspondentes.
- A suíte padrão observou preço 42.50/moeda BRL no SQL, cache vazio antes da leitura, preenchimento após leitura pública e TTL observado de aproximadamente 299.988 segundos. Na falha externa observou 502 pactuado, ausência de registro e chave e consulta pública 404.
- O journal confirmou exatamente uma chamada por SKU/correlação de cada jornada. Nenhum journal bruto foi anexado.
- O verificador comparou resultados e steps com `report/data/test-results`, confirmou arquivos em `report/data/attachments` e a árvore efetiva em `report/widgets/tree.json` com epic/feature/story. O relatório foi gerado com `allurerc.mjs` explícito.
- No diagnóstico, o step esperado ficou `failed`, continha evidência HTTP e a mensagem tinha o marcador de asserção. Não houve falha de conexão ou descoberta disfarçada de diagnóstico.
- As duas rodadas padrão usaram diretórios e identificadores distintos. As limpezas usuais registraram os recursos específicos de cada jornada.
- O segredo sintético atravessou header HTTP real e captura adversarial com query/resposta. O scanner confirmou sua ausência e a da senha local nos bytes UTF-8/UTF-16 dos logs, resultados, anexos e relatório. Dados operacionais permitidos permaneceram legíveis.
- Após as falhas iniciais de conexão no Windows, quatro pedidos haviam sido criados pela API mas não limpos pelo cliente SQL do host. A recuperação leu somente GUIDs de anexos HTTP `POST /orders` com 201, validou-os como GUIDs e executou exclusão restrita a `Id IN (<IDs comprovados>) AND Reference=Id`. Foram excluídas quatro linhas; a consulta posterior mostrou zero pedidos. `DEL` foi aplicado somente às seis chaves comprovadamente criadas nas rodadas, sem varredura ou limpeza global; todas já estavam ausentes. A lista ficou em `artifacts/validation-owned-ids.json`.

## Ajustes e limites observados

As tentativas iniciais sem acesso ao cache/rede restritos falharam ao escrever o cache de templates .NET e baixar npm; foram repetidas com autorização de execução apropriada. O primeiro `docker compose pull` sem `.env` retornou 1 com a mensagem de senha obrigatória; a repetição após configurar o arquivo retornou 0.

O SqlClient apresentou erro nativo 258/TCP usando `localhost`, mesmo quando a porta TCP respondia e a API acessava SQL pela rede Compose. `tcp:127.0.0.1,14333` resolveu o problema. As rodadas `143752519Z-ff22bd04`, `143833998Z-b2a60a2a`, `143925047Z-ac78636d` e `144014037Z-decd3672` retornaram runner 1 e gerador 0, preservando o diagnóstico sanitizado. O helper final utiliza IPv4 explícito.

A primeira restauração com SqlClient 6.1.4 alertou sobre Pkcs 9.0.11. A atualização para SqlClient 6.1.8 resolveu esse aviso; a compilação final ficou sem avisos. O npm reportou seis avisos moderados no conjunto de dependências dev do gerador; não foi feito upgrade automático incompatível. Node 25 gerou aviso de engine em `nanoid`; as rodadas finais usaram Node 24.22.3.

CI: o workflow foi criado, usa os scripts compartilhados e publica com `always()`. O caminho de geração após falha e a preservação do exit code foram exercitados localmente. Não há remote configurado (`git remote -v` vazio), portanto a execução em GitHub Actions e a publicação hospedada não foram verificadas. PowerShell 7/Linux da esteira está configurado, mas o shell efetivamente executado localmente foi Windows PowerShell 5.1. Não foi feita inspeção visual em navegador; a renderização foi conferida pelos dados/arquivos efetivos do relatório.

Não foram adotados xUnit v3/MTP, alternativas de adaptador não exercitadas, idioma pt-BR, single-file, histórico, flakiness, quality gates ou uma suposta opção de desligar analytics. As lacunas correspondentes da pesquisa permanecem explícitas.

## Extensão: guia autossuficiente e SQL reproduzível

Foi adicionado `docs/E2E-GUIDE.md`, com estrutura, responsabilidades, agrupamentos domínio/feature/cenário, versões, configuração, jornadas, evidências, sanitização, reprodução SQL, infraestrutura, scripts, CI, migração e critérios de aceite. A estrutura atual foi diferenciada da organização sugerida para um projeto maior. O documento inclui nove blocos JSON válidos, 17 links internos com âncoras verificadas e blocos de código balanceados.

As operações SQL agora registram o `SqlCommand.CommandText` exato, o parâmetro real `@id` (`uniqueidentifier`/GUID), timeout, classificação de leitura/escrita e `replaySql`. O comando é anexado com estado `prepared` antes do envio; o resultado recebe `completed` somente após o retorno. O script `.sql` usa declaração tipada mais o comando original e é anexado ao mesmo step com MIME `text/plain`. Templates e parâmetros são restritos por uma lista aprovada; SQL arbitrário e valores sensíveis não são publicados. A limpeza é identificada como **ALTERA DADOS**.

| Comando efetivamente executado | Código / resultado |
|---|---|
| `dotnet build Boilerplates.sln --no-restore` | 0; zero avisos e erros |
| `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/stack.ps1 Up` | 0; stack e mocks prontos |
| `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/e2e.ps1 -Sample` | 0; nove testes aprovados, 32 anexos de step e quatro comandos SQL reproduzíveis |
| SELECT aprovado de `replaySql` extraído do anexo, enviado por stdin a `docker compose exec -T sqlserver bash -c 'SQLCMDPASSWORD=$MSSQL_SA_PASSWORD /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -d BoilerplateE2E -b'` | 0; comando executável na base, zero linhas após limpeza automática; nenhuma alteração de dados |
| Verificação local de blocos JSON, links internos e fences do guia com Node | 0; nove JSONs e 17 links válidos |
| `git diff --check` | 0 |

Execução: `20261114T152138718Z-e4aeecc3`; relatório: `artifacts/20261114T152138718Z-e4aeecc3/report`. `execution.json` registra runner=0, gerador=0 e verificador=0. O verificador adicional conferiu igualdade entre texto aprovado, parâmetros, script JSON e arquivo `.sql` no mesmo step; a verificação existente confirmou anexos renderizados, árvore BDD e ausência dos segredos sintético/local. O replay de limpeza não foi executado manualmente; ele foi validado por conteúdo e pela própria limpeza da jornada.

Ao final, `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/stack.ps1 Down` retornou 0; os quatro containers e a rede foram removidos, preservando os artefatos locais.
