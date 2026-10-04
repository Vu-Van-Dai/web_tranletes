# Runs the load tool against a real Kestrel instance constrained to 2 logical processors (Oracle A1-like).
# Uses a dedicated database "comicplatform_load" (dropped and recreated) — no other database is touched.
param(
  [int]$Port = 5182,
  [int]$Processors = 2,
  [int]$ImageConcurrency = 2,
  [string]$Report = "$PSScriptRoot/../../../docs/benchmarks/benchmark-local.md"
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/../../.."
$envFile = Get-Content "$root/.env" -Raw
$cs = ([regex]::Match($envFile, '(?m)^DATABASE_CONNECTION_STRING=(.*)$').Groups[1].Value).Trim()
$password = ([regex]::Match($cs, 'Password=([^;]*)').Groups[1].Value)
$psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
$env:PGPASSWORD = $password
& $psql -h localhost -U postgres -w -q -c "DROP DATABASE IF EXISTS comicplatform_load WITH (FORCE);" -c "CREATE DATABASE comicplatform_load ENCODING 'UTF8' TEMPLATE template0;"
$loadCs = "Host=localhost;Database=comicplatform_load;Username=postgres;Password=$password;Maximum Pool Size=30"
$data = "$root/data/load"
Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue

dotnet build "$root/backend/src/Api" -c Release -nologo -v q | Out-Null
$apiEnv = @{
  ASPNETCORE_ENVIRONMENT = 'Benchmark'; ASPNETCORE_URLS = "http://localhost:$Port"; DOTNET_PROCESSOR_COUNT = "$Processors"
  DATABASE_CONNECTION_STRING = $loadCs; JWT_SECRET = 'benchmark-only-jwt-secret-0123456789-abcdefghij'
  SEED_ADMIN_EMAIL = 'load@bench.test'; SEED_ADMIN_USERNAME = 'load_admin'; SEED_ADMIN_PASSWORD = 'Load-Admin-Password-123'
  PACKAGE_STORE = 'local'; COMIC_STORAGE_PATH = "$data/comics"; TEMP_STORAGE_PATH = "$data/tmp"; PUBLIC_IMAGE_URL = '/cdn'
  RATE_LIMITS_ENABLED = 'false'; IMAGE_PROCESSING_CONCURRENCY = "$ImageConcurrency"; MIN_FREE_SPACE = '10MB'
}
foreach ($k in $apiEnv.Keys) { Set-Item "env:$k" $apiEnv[$k] }
$api = Start-Process dotnet -ArgumentList "$root/backend/src/Api/bin/Release/net10.0/ComicPlatform.Api.dll" -PassThru -WindowStyle Hidden `
  -RedirectStandardOutput "$env:TEMP/cp-load-api.log" -RedirectStandardError "$env:TEMP/cp-load-api.err"
try {
  for ($i = 0; $i -lt 60; $i++) { try { if ((Invoke-WebRequest "http://localhost:$Port/api/health" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200) { break } } catch { Start-Sleep 1 } }
  New-Item -ItemType Directory -Force (Split-Path $Report) | Out-Null
  dotnet run --project "$root/backend/tests/LoadTests" -c Release -- "http://localhost:$Port" load_admin Load-Admin-Password-123 $loadCs $Report
}
finally {
  Stop-Process -Id $api.Id -Force -ErrorAction SilentlyContinue
}
