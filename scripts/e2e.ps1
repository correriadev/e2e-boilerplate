param(
    [ValidateSet('Standard','Diagnostic','Proof','Configuration','Http')][string]$Mode = 'Standard',
    [switch]$Sample
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Push-Location "$PSScriptRoot/.."
$previousAllure = $env:ALLURE_CONFIG
$previousDiagnostic = $env:E2E_DIAGNOSTIC
$runner = 2
$generator = -1
$validation = -1
try {
    if ($Sample) { . "$PSScriptRoot/sample-env.ps1" }
    $runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
    $run = Join-Path (Get-Location) "artifacts/$runId"
    $results = Join-Path $run 'results'
    $report = Join-Path $run 'report'
    New-Item -ItemType Directory -Path $results -Force | Out-Null
    $config = @{ allure = @{ directory = $results; failExceptions = @('Xunit.Sdk.XunitException'); xunitRunnerReporter = 'none' } }
    $env:ALLURE_CONFIG = Join-Path $run 'adapter.json'
    [IO.File]::WriteAllText($env:ALLURE_CONFIG, ($config | ConvertTo-Json -Depth 5))
    $env:E2E_DIAGNOSTIC = if ($Mode -eq 'Diagnostic') { '1' } else { '0' }
    $filters = @{ Standard='Category!=Diagnostic'; Diagnostic='Category=Diagnostic'; Proof='Category=Proof'; Configuration='Category=Configuration'; Http='Category=Http' }
    Write-Host "Execução: $runId / $Mode"
    # Native nonzero exit is handled explicitly on both Windows PowerShell 5.1 and pwsh.
    $ErrorActionPreference = 'Continue'
    dotnet test tests/Boilerplate.E2E/Boilerplate.E2E.csproj --settings e2e.runsettings --filter $filters[$Mode] --logger "trx;LogFileName=tests.trx" --results-directory $run 2>&1 |
        Tee-Object -FilePath (Join-Path $run 'runner.log') | Out-Host
    $runner = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    $fresh = @(Get-ChildItem $results -Filter '*-result.json')
    if ($fresh.Count -gt 0) {
        $ErrorActionPreference = 'Continue'
        node node_modules/allure/cli.js generate $results --config allurerc.mjs --output $report 2>&1 |
            Tee-Object -FilePath (Join-Path $run 'generator.log') | Out-Host
        $generator = $LASTEXITCODE
        node scripts/verify-artifacts.mjs $run $Mode
        $validation = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
    } else {
        Write-Host 'Nenhum resultado novo: confira descoberta, build e carregamento do reporter.'
    }
    $exitCode = if ($runner -ne 0) { $runner } elseif ($fresh.Count -eq 0) { 3 } elseif ($generator -ne 0) { 4 } elseif ($validation -ne 0) { 5 } else { 0 }
    @{ runId=$runId; mode=$Mode; runnerExit=$runner; generatorExit=$generator; validationExit=$validation; resultCount=$fresh.Count; report=$report; exitCode=$exitCode } |
        ConvertTo-Json | Set-Content (Join-Path $run 'execution.json') -Encoding UTF8
    Write-Host "Runner=$runner; gerador=$generator; validação=$validation; relatório=$report"
    exit $exitCode
} finally {
    $env:ALLURE_CONFIG = $previousAllure
    $env:E2E_DIAGNOSTIC = $previousDiagnostic
    Pop-Location
}
