param(
    [string]$Root = "D:\CodeNetSpace\AM.TribonAutomationProbe"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Root = (Resolve-Path -LiteralPath $Root).Path

$module = Join-Path `
    $Root `
    "vitesse\AddIns\AMGeometryObjectAutomation\geometry_label_plan_binding.py"

if (-not (Test-Path -LiteralPath $module -PathType Leaf)) {
    throw "Vitesse plan-binding module not found: $module"
}

$command = Get-Command py -ErrorAction SilentlyContinue
$arguments = @("-3", $module)

if ($null -eq $command) {
    $command = Get-Command python -ErrorAction SilentlyContinue
    $arguments = @($module)
}

if ($null -eq $command) {
    throw "Python 3 was not found for the offline parity self-test."
}

& $command.Source @arguments

if ($LASTEXITCODE -ne 0) {
    throw "Vitesse plan-binding self-test failed."
}

Write-Host "ROUND4_3A2_VITESSE_PLAN_BINDING=PASS"