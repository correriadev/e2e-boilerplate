# Research: Allure 3 para E2E de API (`allure-e2e-api-evidencias`)

- **Overarching Question:** Como relatórios Allure são estruturados para testes E2E de API na versão Allure 3, quais evidências anexar por cenário e como apresentá-las.
- **Updated:** 2026-11-13 | **Status:** `complete` (5/5 MACROs); micros com lacunas, ver §4
- **Sources Inspected:** `web` (allurereport.org, cross-versão marcada; GitHub só como fonte não oficial)
- **Coverage:** `5/5 MACROs completed, 8/30 MICROs answered` (20 partial, 2 blocked)

## 1. TL;DR (Executive Answer)
- **Estrutura:** Allure 3 não tem árvore fixa; o agrupamento vem de `groupBy` no plugin Awesome. Presets suite (`parentSuite/suite/subSuite`, até 3 níveis) e BDD (`epic/feature/story`) são os oficiais [M1.1][M2.2].
- **Configuração:** arquivo `allurerc.{js,mjs,cjs,json,yaml,yml}`; `allureConfig.json` (formato Allure 2) não aparece como lido pela v3 [M2.1]. `reportName`, `reportLanguage`, `singleFile` e `groupBy` ficam em `plugins.awesome.options` [M2.2][M5.5].
- **Evidências por cenário:** `application/json` e `text/plain` são suportados; `application/vnd.allure.http+json` existe, mas a construção documentada é Java [M3.1]. Sanitizar antes de anexar: `Masked` só oculta na tabela, `Hidden` tira do relatório mas o valor continua em `allure_results` [M3.3].
- **Lacuna central:** não há doc oficial para "conjunto mínimo de evidências", Dado/Quando/Então nem para severity em classe+método. Essas partes são síntese própria [M3.2][M3.4][M1.6].
- **Privacidade:** nenhuma opção documentada para desligar analytics no Allure 3; o status é incerto [M5.1].
- **Restrição:** páginas .NET (NUnit, xUnit) são cross-versão; o pacote .NET compatível com v3 não foi confirmado [M5.4].

## 2. Findings Matrix & Shard Router
| ID | Aspect / Question | Key Verdict | Confidence | Shard Pointer | Primary Source |
|---|---|---|---|---|---|
| `M1.1` | Árvore e groupBy | groupBy com presets suite, BDD, package, module; até 3 níveis | `verified` | [shards/M1.md](shards/M1.md) | `allurereport.org/docs/v3/configure/` |
| `M1.2` | Semântica epic/feature/story p/ API | doc define epic>feature>story; uso em API é inferência | `inferred` | [shards/M1.md](shards/M1.md) | `allurereport.org/docs/v3/navigation/` |
| `M1.3` | owner, tags, severity | severity: trivial, minor, normal, critical, blocker; owner livre; tags sem lista fechada | `verified` | [shards/M1.md](shards/M1.md) | `allurereport.org/docs/v3/readability/` |
| `M1.4` | IDs e vínculos | AllureId, AllureTms, AllureLink, AllureIssue (.NET); vínculo com requisito sem label próprio | `verified` | [shards/M1.md](shards/M1.md) | `allurereport.org/docs/xunit-reference/` |
| `M1.5` | Labels automáticas do .NET | lista não encontrada; `hideLabels` oculta só na UI | `unresolved` | [shards/M1.md](shards/M1.md) | `allurereport.org/docs/v3/configure/` |
| `M1.6` | Severity classe vs método | sem fonte; precedência não documentada | `unresolved` | [shards/M1.md](shards/M1.md) | — (bloqueado) |
| `M2.1` | Nome e precedência do allurerc | 6 extensões aceitas; primeiro encontrado vence; allureConfig.json não confirmado como lido | `verified` | [shards/M2.md](shards/M2.md) | `allurereport.org/docs/v3/configure/`, `/v3/migrate/` |
| `M2.2` | plugins.awesome.options | reportName, reportLanguage (default `en`), singleFile (default `false`), groupBy | `verified` | [shards/M2.md](shards/M2.md) | `allurereport.org/docs/v3/configure/` |
| `M2.3` | categories | `rules[]` com `name` + `matchers`; objeto=AND, array=OR; groupBy built-in ou `{label}` | `verified` | [shards/M2.md](shards/M2.md) | `allurereport.org/docs/categories/` |
| `M2.4` | environments e variables | variables = metadados no topo; environments exigem `matcher` sobre labels | `verified` | [shards/M2.md](shards/M2.md) | `allurereport.org/docs/v3/configure/` |
| `M2.5` | history e flaky | historyPath (JSONL), historyLimit, flakyDetection (historyDepth 10, stabilizationPeriod 5) | `verified` | [shards/M2.md](shards/M2.md) | `allurereport.org/docs/v3/configure/` |
| `M2.6` | environment.properties / executor.json | não confirmados como v3 em página lida | `inferred` | [shards/M2.md](shards/M2.md) | `how-it-works-environment-file/`, `how-it-works-executor-file/` |
| `M3.1` | Media types, AddAttachment, limite | text/plain, application/json suportados; sem limite de tamanho achado | `verified` | [shards/M3.md](shards/M3.md) | `allurereport.org/docs/attachments/` |
| `M3.2` | Steps HTTP, Dado/Quando/Então | steps em árvore aceitam anexos; Dado/Quando/Então sem fonte oficial | `inferred` | [shards/M3.md](shards/M3.md) | `allurereport.org/docs/steps/` |
| `M3.3` | ParameterMode | Default mostra; Masked oculta valor; Hidden tira do relatório (valor fica em allure_results) | `verified` | [shards/M3.md](shards/M3.md) | `allurereport.org/docs/nunit-reference/` |
| `M3.4` | Evidências mínimas e sanitização | sem fonte oficial; lista é síntese própria; sanitizar antes do anexo | `inferred` | [shards/M3.md](shards/M3.md) | `allurereport.org/docs/attachments/` |
| `M3.5` | AllureDescription | Markdown, HTML removido; gravado em `description`; local de exibição não documentado | `verified` | [shards/M3.md](shards/M3.md) | `allurereport.org/docs/nunit-reference/` |
| `M3.6` | Erro, stack, failExceptions | `statusDetails.message/trace`; step sintético no Allure 3; regra por tipo não confirmada | `verified` | [shards/M3.md](shards/M3.md) | `allurereport.org/docs/steps/` |
| `M4.1` | Categorias padrão | Product errors (failed), Test errors (broken); ambiente/dados/infra sem categoria padrão | `inferred` | [shards/M4.md](shards/M4.md) | `allurereport.org/docs/categories/` (cross-versão) |
| `M4.2` | Regex por message/trace | `categories.rules[].matchers` com `message` e `trace`; AND/OR; primeira regra que casa | `verified` | [shards/M4.md](shards/M4.md) | `allurereport.org/docs/categories/` |
| `M4.3` | Flaky detection | ≥2 mudanças de status em até 10 resultados do mesmo ambiente; sintaxe de config não lida | `verified` | [shards/M4.md](shards/M4.md) | `allurereport.org/docs/test-stability/` |
| `M4.4` | Retries no relatório | resultado mais recente + retries; "Changed on retry"; `--rerun` sem default informado | `verified` | [shards/M4.md](shards/M4.md) | `allurereport.org/docs/history-and-retries/` |
| `M4.5` | qualityGate | built-in, exit 1; maxFailures, minTestsCount, successRate, maxDuration; chave exata não lida | `inferred` | [shards/M4.md](shards/M4.md) | `allurereport.org/docs/quality-gate/` |
| `M4.6` | Layout, charts, sections v3 | não pesquisado (budget web esgotado) | `unresolved` | [shards/M4.md](shards/M4.md) | sitemap (candidata: visual-analytics) |
| `M5.1` | Analytics / telemetria | nenhuma opção documentada na v3; Allure 2 tinha `ALLURE_NO_ANALYTICS`; status na v3 incerto | `unresolved` | [shards/M5.md](shards/M5.md) | `allurereport.org/docs/v3/configure/`; issue/PR GitHub (não oficial) |
| `M5.2` | singleFile | `true` gera HTML autônomo; anexos embutidos não confirmados | `inferred` | [shards/M5.md](shards/M5.md) | `allurereport.org/docs/v3/configure/` |
| `M5.3` | Metadados de CI | lista exata não encontrada; sensibilidade inferida | `inferred` | [shards/M5.md](shards/M5.md) | `allurereport.org/docs/how-it-works/` |
| `M5.4` | Adaptador .NET compatível | gera arquivos Allure 2, compatível em princípio; pacote/versão não confirmados | `inferred` | [shards/M5.md](shards/M5.md) | `allurereport.org/docs/v3/migrate/`, `/docs/xunit/` |
| `M5.5` | Idiomas | `reportLanguage` default `en`; lista e pt-BR não confirmados | `unresolved` | [shards/M5.md](shards/M5.md) | `allurereport.org/docs/v3/configure/` |
| `M5.6` | Limpeza e pasta de saída | output default `./allure-report` (`--output`); sem opção de limpeza documentada | `unresolved` | [shards/M5.md](shards/M5.md) | `allurereport.org/docs/v3/configure/` |

## 3. Macro Syntheses
### M1: Hierarquia e labels ([shards/M1.md](shards/M1.md))
- Árvore depende de `groupBy`; presets suite e BDD são oficiais; BDD (`epic/feature/story`) é o que se mapeia a jornada de API [M1.1][M1.2].
- Severity tem valores fechados (`trivial`…`blocker`); critério de escolha não é oficial [M1.3].
- IDs estáveis: `AllureId`/`AllureTms` no adaptador .NET; vínculo com requisito via `AllureLink`/`AllureIssue` [M1.4].
- Labels automáticas do .NET não listadas nas fontes; ocultar com `hideLabels` (efeito só na UI) [M1.5].
- Severity duplicada entre classe e método: sem resposta documentada [M1.6].

### M2: Configuração do Allure 3 ([shards/M2.md](shards/M2.md))
- Usar `allurerc.*` (ex.: `allurerc.mjs`); não assumir leitura de `allureConfig.json` [M2.1].
- Em `plugins.awesome.options`: `reportName`, `reportLanguage`, `singleFile`, `groupBy` [M2.2].
- `categories.rules[]` com `matchers` (`message`, `trace`) e precedência por ordem [M2.3].
- `variables` (metadados globais) e `environments` (matcher sobre labels) [M2.4].
- `historyPath` + `historyLimit` + `flakyDetection`; flaky não tem efeito sem histórico [M2.5].

### M3: Evidências de E2E de API ([shards/M3.md](shards/M3.md))
- Anexar JSON com `application/json` e texto com `text/plain`; tipo sem preview vira link de download [M3.1].
- Steps para cada chamada HTTP, com anexos aninhados; títulos descritivos (exemplos oficiais em Java) [M3.2].
- `Masked` para dado sensível visível; `Hidden` não basta, o valor fica em `allure_results` [M3.3].
- Conjunto mínimo (requisição, resposta, estado, mocks, correlação) é síntese própria; sanitizar antes do anexo [M3.4].
- Descrição em Markdown via `[AllureDescription]`; falha vai para `statusDetails` [M3.5][M3.6].

### M4: Categorias, flakiness e histórico ([shards/M4.md](shards/M4.md))
- Defaults: Product errors (failed) e Test errors (broken); ambiente/dados/infra exigem categoria customizada, sem exemplo [M4.1].
- Regex por `message` e `trace` com AND/OR; primeira regra que casa vence [M4.2].
- Flaky: ≥2 mudanças de status nos últimos 10 resultados do mesmo ambiente [M4.3].
- Retries aparecem como tentativas separadas; resultado mais recente na lista [M4.4].
- `qualityGate` existe (exit 1 ao violar regra); chave exata não confirmada [M4.5].
- Layout, charts e sections não pesquisados [M4.6].

### M5: Apresentação, privacidade e operação ([shards/M5.md](shards/M5.md))
- Sem opt-out documentado de analytics na v3; decidir com teste de rede no relatório gerado [M5.1].
- `singleFile: true` para um HTML único; compartilhamento como evidência é inferência [M5.2].
- Campos de CI não listados; URL de build, branch e anexos podem expor infra ou dados de conta/PIX [M5.3].
- Adaptador .NET gera formato Allure 2; versão do pacote a confirmar [M5.4].
- `reportLanguage` existe; pt-BR não confirmado [M5.5].
- Pasta de saída `--output`; limpeza de resultados antigos é prática, não doc oficial [M5.6].

## 4. Open Gaps & Contradictions
- **[M1.6 / Unresolved]:** precedência de severity entre classe e método; fonte não encontrada (micro bloqueado).
- **[M4.6 / Unresolved]:** layout, charts e sections da v3; budget web esgotado (micro bloqueado).
- **[M1.2 / Inferred]:** mapeamento epic/feature/story para E2E de API é síntese própria, sem fonte.
- **[M1.5 / Unresolved]:** labels automáticas do adaptador .NET não listadas.
- **[M2.1 / Conflict]:** allurerc.ts aparece em resumo de busca, mas não na lista da página lida. Precedência entre arquivo, env var e CLI não confirmada por citação.
- **[M2.2 / Unresolved]:** valores aceitos de `reportLanguage` não encontrados; resumo e página divergem sobre `groupBy` do plugin.
- **[M2.6 / Inferred]:** `environment.properties` e `executor.json` vêm de resumo; não confirmados em página lida.
- **[M3.2 / Unresolved]:** Dado/Quando/Então sem fonte oficial; `[AllureStep]` (.NET) só por resumo.
- **[M3.3 / Inferred]:** efeito de `excluded` no relatório Allure 3 não confirmado (página NUnit atribui ao TestOps).
- **[M3.1 / Inferred]:** HTTP exchange (`application/vnd.allure.http+json`) documentado só para Java 3.0+; equivalente .NET não verificado.
- **[M3.1 / Unresolved]:** limite de tamanho de anexo não encontrado (ausência nas páginas lidas, não prova).
- **[M4.1 / Inferred]:** categorias padrão são de páginas cross-versão; ambiente/dados/infra sem exemplo.
- **[M4.5 / Inferred]:** formato exato da chave `qualityGate`; URL de configuração retornou 404 em 2026-11-13.
- **[M5.1 / Unresolved]:** ausência de opt-out na v3 é ausência nas páginas lidas; issue #476 e PR #784 do allure3 são fontes GitHub não oficiais, com status incerto.
- **[M5.4 / Inferred]:** pacote `Allure.Xunit`/`Allure.Net` compatível com v3 não confirmado; páginas .NET lidas são cross-versão.
- **[M5.5 / Unresolved]:** lista de idiomas e suporte a pt-BR não confirmados.
- **[M5.6 / Unresolved]:** nenhuma opção de limpeza documentada; página de CLI não alcançada.
- **[Método]:** excertos vieram de WebFetch (resumo processado por modelo) e não foram conferidos byte a byte no HTML. Antes de usar em documento oficial, reconferir.
- **[Método]:** seis MACROs de busca consumiram o budget; itens `inferred` não têm citação verbatim.

## 5. Agent Consumption Protocol
1. **Fast Triage (`fast-scope` / quick lookup):** Read ONLY `## 1. TL;DR` and `## 2. Findings Matrix`.
2. **Specification Grounding (`fast-scope` / `qa-scope` / `pbb-design`):** Read `RESEARCH.md` in full, then read only the `## 1. Executive Summary` (lines 1–35) of relevant shards.
3. **Deep Implementation / Audit (`fast-code` / `the-grumpy-tech-lead`):** Open the specific `shards/<macro_id>.md` cited in `## 2. Findings Matrix` and jump to `### <micro_id>` under `## 2. Deep Evidence` for verbatim quotes and exact locators.
