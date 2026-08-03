param(
    [string]$Root = "D:\CodeNetSpace\AM.TribonAutomationProbe"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Root = (Resolve-Path -LiteralPath $Root).Path

$workerPath = Join-Path $Root "vitesse\AddIns\AMGeometryObjectAutomation\Start.py"
$worker = [System.IO.File]::ReadAllText($workerPath)
foreach ($forbidden in @(
        'import json',
        'json.loads',
        '(?m)^\s*b"',
        "(?m)^\s*b'",
        '\bbytes\b',
        '\bwith\s+open\b',
        '\bpathlib\b',
        '\bdataclass\b',
        'GetX\s*\(',
        'GetY\s*\(')) {
    if ($worker -match $forbidden) {
        throw "Forbidden Python 2.3/runtime dependency or syntax found: $forbidden"
    }
}
if ($worker -notmatch 'def _bootstrap_status\(' -or
    $worker -notmatch '_bootstrap_status\("MODULE_STARTED"' -or
    $worker -notmatch '_bootstrap_status\("KCS_IMPORTS_OK"' -or
    $worker -notmatch '_bootstrap_status\("PLAN_BINDING_DEFINED"' -or
    $worker -notmatch '_bootstrap_status\("ADDIN_ROOT_RESOLVED"' -or
    $worker -notmatch '_bootstrap_status\("DIRECT_ENTRY_CHECK"' -or
    $worker -notmatch '_bootstrap_status\("RUN_STARTED"') {
    throw "Bootstrap diagnostics contract is incomplete."
}
if ($worker -notmatch 'def _string_array_field\(' -or
    $worker -notmatch 'def _sha256_fallback\(' -or
    $worker -notmatch 'SHA256_EMPTY_EXPECTED' -or
    $worker -notmatch 'SHA256_ABC_EXPECTED' -or
    $worker -notmatch 'F2B14D4200E1AC239FBF1CFD28D2F99439E631EC2D6FA129ECB6A92A841B75F2') {
    throw "Inline parser/hash fallback contract is incomplete."
}
if ($worker -notmatch 'def _valid_addin_root\(' -or
    $worker -notmatch 'ADDIN_ROOT, ADDIN_ROOT_SOURCE = _resolve_addin_root\(\)' -or
    $worker -notmatch 'candidates\.append\(\(cwd, "CWD"\)\)' -or
    $worker -notmatch 'candidates\.append\(\(file_root, "FILE"\)\)' -or
    $worker -notmatch 'except SystemExit, error' -or
    $worker -notmatch 'def _write_failure_result_for_selected\(') {
    throw "Staging root and selected-request failure contract is incomplete."
}
$markers = @('REQUEST_SELECTED','TEXT_CAPTURE_START','TEXT_CAPTURE_RETURNED','TEXT_PROPERTIES_GET_START','TEXT_PROPERTIES_GET_RETURNED','ELEMENT_EXTENT_GET_START','ELEMENT_EXTENT_GET_RETURNED','DETECTOR_START','DETECTOR_RETURNED','PLAN_READ_START','PLAN_READ_RETURNED','TARGET_RESOLVE_START','TARGET_RESOLVE_RETURNED','LABEL_INDEX_START','LABEL_INDEX_RETURNED','PREFLIGHT_EVALUATE_START','PREFLIGHT_EVALUATE_RETURNED','PLAN_BINDING_START','PLAN_BINDING_RETURNED','RESULT_WRITE_START','RESULT_WRITE_RETURNED','REQUEST_ARCHIVE_START','REQUEST_ARCHIVE_RETURNED','PROCESS_SUCCESS','PROCESS_EXCEPTION','FAILURE_RESULT_WRITE_START','FAILURE_RESULT_WRITE_RETURNED','FAILURE_ARCHIVE_START','FAILURE_ARCHIVE_RETURNED')
foreach ($marker in $markers) {
    if ($worker -notmatch ('"' + $marker + '"')) { throw "Missing stage marker: $marker" }
}
if ($worker -notmatch 'stage-trace\.txt.*"ab"' -and $worker -notmatch 'stage-trace\.txt", "ab"') { throw "Stage trace is not append-only." }
if ($worker -match 'SAVEWORK') { throw "SAVEWORK is forbidden." }
if ($worker -notmatch 'SetBoundaryInfinite\(\)') { throw "Capture boundary policy changed." }
$moduleBeforeRoot = $worker.IndexOf('import kcs_draft', [System.StringComparison]::Ordinal)
$bootstrapBeforeKcs = $worker.IndexOf('_bootstrap_status("MODULE_STARTED"', [System.StringComparison]::Ordinal)
if ($bootstrapBeforeKcs -lt 0 -or $bootstrapBeforeKcs -gt $moduleBeforeRoot) {
    throw "Bootstrap must run before KCS imports."
}

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
