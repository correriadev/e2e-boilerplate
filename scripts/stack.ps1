param([ValidateSet('Up','Check','Down')][string]$Action = 'Up')
$ErrorActionPreference = 'Stop'
Push-Location "$PSScriptRoot/.."
try {
    . "$PSScriptRoot/sample-env.ps1"
    if ($Action -eq 'Down') {
        docker compose down --remove-orphans
        exit $LASTEXITCODE
    }
    if ($Action -eq 'Up') {
        docker compose up -d --build --wait --wait-timeout 180
        if ($LASTEXITCODE -ne 0) { throw 'docker compose up falhou.' }
    }
    $ready = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(180)
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            $found = Invoke-RestMethod 'http://localhost:9090/catalog/products/SKU-E2E' -TimeoutSec 3
            $failureStatus = 0
            try { Invoke-WebRequest 'http://localhost:9090/catalog/products/SKU-UNAVAILABLE' -UseBasicParsing -TimeoutSec 3 | Out-Null }
            catch { if ($_.Exception.Response) { $failureStatus = [int]$_.Exception.Response.StatusCode } }
            $health = Invoke-RestMethod "$($env:E2E_BASEURL.TrimEnd('/'))/health/ready" -TimeoutSec 3
            if ($found.price -eq 42.50 -and $found.currency -eq 'BRL' -and $failureStatus -eq 503 -and $health.code -eq 'ready') {
                $ready = $true
                break
            }
        } catch { } # Never print connection exception payloads.
        Start-Sleep -Seconds 2
    }
    if (-not $ready) { throw 'Prontidão não confirmada em 180 segundos: verifique API, schema, Redis e os dois mappings.' }
    Write-Host 'Pronto: catálogo 200/42.50/BRL, catálogo indisponível 503 e API ready 200.'
} finally { Pop-Location }
