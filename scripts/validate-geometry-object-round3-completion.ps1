$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet build AM.TribonAutomationProbe.sln
    $test = dotnet test AM.TribonAutomationProbe.sln --no-build 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Round 3 tests failed." }
    powershell -ExecutionPolicy Bypass -File scripts\validate-vitesse-geometry-object-automation.ps1
    Write-Host "PASS: Round 3 C# completion validation"
} finally { Pop-Location }
