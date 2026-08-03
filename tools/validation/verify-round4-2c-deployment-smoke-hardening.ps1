$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scripts = @(Get-ChildItem (Join-Path $root "tools\validation") -Filter "*round4-2c*.ps1") + @(Get-ChildItem (Join-Path $root "scripts") -Filter "*round4-2c*.ps1")
foreach ($file in $scripts) {
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    if ($bytes.Length -lt 3 -or $bytes[0] -ne 239 -or $bytes[1] -ne 187 -or $bytes[2] -ne 191) { throw "Missing UTF-8 BOM: $($file.Name)" }
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
    if (@($errors).Count -gt 0) { throw "PowerShell parse error: $($file.Name)" }
    $text = [IO.File]::ReadAllText($file.FullName, [Text.Encoding]::UTF8)
    if ($file.Name -ne "verify-round4-2c-deployment-smoke-hardening.ps1" -and ($text.Contains("璇嗗埆") -or $text.Contains("鍥剧焊"))) { throw "Mojibake found: $($file.Name)" }
}
$interpret = [IO.File]::ReadAllText((Join-Path $root "tools\validation\smoke-round4-2c-test-machine-interpret.ps1"), [Text.Encoding]::UTF8)
$compound = [IO.File]::ReadAllText((Join-Path $root "tools\validation\smoke-round4-2c-llm-filebridge-tribon-detect-highlight-flanges.ps1"), [Text.Encoding]::UTF8)
if (-not $interpret.Contains(([char]0x8BC6 + [char]0x522B)) -or -not $compound.Contains(([char]0x5148 + [char]0x8BC6))) { throw "Required Chinese text missing" }
& powershell -ExecutionPolicy Bypass -File (Join-Path $root "scripts\package-round4-2c-readonly.ps1")
$manifest = Join-Path $root "artifacts\evidence\round4-2c-test-console-package-manifest.tsv"
$header = Get-Content $manifest -TotalCount 1
if ($header -ne "path`tlength`tsha256") { throw "Manifest is not real TSV" }
$rows = Import-Csv $manifest -Delimiter "`t"
if (@($rows).Count -lt 5) { throw "Manifest too small" }
Write-Output "ROUND4_2C_DEPLOYMENT_SMOKE_HARDENING=PASS"

