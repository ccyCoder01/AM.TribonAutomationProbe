$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
dotnet build (Join-Path $root "AM.TribonAutomationProbe.sln") --no-restore
dotnet test (Join-Path $root "AM.TribonAutomationProbe.sln") --no-build
if ($env:ASSISTANT_API_KEY) { throw "API key must not be read by offline verification." }
$exe = Join-Path $root "src\AM.TribonAutomationProbe.Console\bin\Debug\net8.0\AM.TribonAutomationProbe.Console.exe"
$out = & $exe "assistant-run" "--text=detect geometry" 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { throw $out }
$json = $out | ConvertFrom-Json
if ($json.executionRequested -ne $false -or $json.executionPerformed -ne $false -or $json.savePerformed -ne $false) { throw "Offline assistant-run unexpectedly executed." }
Write-Output "ROUND4_2C_OFFLINE=PASS"

