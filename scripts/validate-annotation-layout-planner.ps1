$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet build AM.TribonAutomationProbe.sln --no-restore
    dotnet test tests\AM.TribonAutomationProbe.Tests\AM.TribonAutomationProbe.Tests.csproj --no-build
    $fixture = Join-Path $root "artifacts\layout-fixtures"
    New-Item -ItemType Directory -Force -Path $fixture | Out-Null
    $clean = Join-Path $fixture "clean.json"
    $conflict = Join-Path $fixture "n4-s1.json"
    Set-Content -LiteralPath $clean -Encoding utf8 -Value '{"schemaVersion":"1.0","scope":"current_drafting_context","handleScope":"current_drafting_session_only","drawingExtent":{"x1":0,"y1":0,"x2":287,"y2":200},"items":[]}'
    Set-Content -LiteralPath $conflict -Encoding utf8 -Value '{"schemaVersion":"1.0","scope":"current_drafting_context","handleScope":"current_drafting_session_only","drawingExtent":{"x1":0,"y1":0,"x2":287,"y2":200},"items":[{"role":"movable","type":"position_number","runtimeHandle":"N4","parentExtent":{"x1":24.7831039429,"y1":132.348770142,"x2":29.1581039429,"y2":134.848770142},"labelExtent":{"x1":24.7831039429,"y1":132.348770142,"x2":29.1581039429,"y2":134.848770142},"text":"N4"},{"role":"obstacle","type":"position_number","runtimeHandle":"S1","parentExtent":{"x1":24.7831039429,"y1":132.348770142,"x2":29.1581039429,"y2":134.848770142},"labelExtent":{"x1":24.7831039429,"y1":132.348770142,"x2":29.1581039429,"y2":134.848770142},"text":"S1"}]}'
    $exe = Join-Path $root "src\AM.TribonAutomationProbe.Console\bin\Debug\net8.0\AM.TribonAutomationProbe.Console.exe"
    $cleanPlan = Join-Path $fixture "clean-plan.json"
    $conflictPlan = Join-Path $fixture "conflict-plan.json"
    & $exe plan-annotation-layout "--adapter=mock" "--snapshot=$clean" "--output=$cleanPlan"
    & $exe plan-annotation-layout "--adapter=mock" "--snapshot=$conflict" "--output=$conflictPlan"
    if (-not (Test-Path $cleanPlan)) { throw "clean plan missing" }
    if (-not (Test-Path $conflictPlan)) { throw "conflict plan missing" }
    Write-Host "PASS: annotation layout planner validation"
} finally { Pop-Location }
