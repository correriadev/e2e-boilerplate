# Dot-source only when using the local sample. Existing process environment wins.
if (-not $env:SQLSERVER_SA_PASSWORD -and (Test-Path "$PSScriptRoot/../.env")) {
    foreach ($line in [IO.File]::ReadAllLines((Join-Path $PSScriptRoot '../.env'))) {
        if ($line -match '^SQLSERVER_SA_PASSWORD=(.+)$') { $env:SQLSERVER_SA_PASSWORD = $Matches[1].Trim() }
    }
}
if (-not $env:SQLSERVER_SA_PASSWORD) { throw 'Preencha SQLSERVER_SA_PASSWORD no .env ou ambiente.' }
if (-not $env:E2E_BASEURL) { $env:E2E_BASEURL = 'http://localhost:5080' }
if (-not $env:E2E_SQLSERVER_CONNECTIONSTRING) {
    # IPv4 loopback avoids the SqlClient native TCP timeout observed with localhost on Windows.
    # Quoting supports semicolons and double quotes in a supplied local password.
    $quotedPassword = '"' + $env:SQLSERVER_SA_PASSWORD.Replace('"', '""') + '"'
    $env:E2E_SQLSERVER_CONNECTIONSTRING = 'Server=tcp:127.0.0.1,14333;Database=BoilerplateE2E;User Id=sa;Password=' + $quotedPassword + ';Encrypt=True;TrustServerCertificate=True;Connect Timeout=5'
}
if (-not $env:E2E_REDIS_CONNECTIONSTRING) { $env:E2E_REDIS_CONNECTIONSTRING = 'localhost:16379' }
if (-not $env:E2E_WIREMOCK_ADMIN_URL) { $env:E2E_WIREMOCK_ADMIN_URL = 'http://localhost:9090/' }
