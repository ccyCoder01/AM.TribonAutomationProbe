param(
    [string]$Root = "D:\CodeNetSpace\AM.TribonAutomationProbe"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Root = (Resolve-Path -LiteralPath $Root).Path

$workerPath = Join-Path $Root "vitesse\AddIns\AMGeometryObjectAutomation\Start.py"
$workerBytes = [System.IO.File]::ReadAllBytes($workerPath)
$crlfCount = 0
$lfCount = 0
$loneCrCount = 0
$nul = $false
$ascii = $true
for ($index = 0; $index -lt $workerBytes.Length; $index++) {
    if ($workerBytes[$index] -eq 0) { $nul = $true }
    if ($workerBytes[$index] -ge 128) { $ascii = $false }
    if ($workerBytes[$index] -eq 10) {
        $lfCount++
        if ($index -gt 0 -and $workerBytes[$index - 1] -eq 13) { $crlfCount++ }
    }
    if ($workerBytes[$index] -eq 13 -and ($index + 1 -ge $workerBytes.Length -or $workerBytes[$index + 1] -ne 10)) { $loneCrCount++ }
}
$loneLfCount = $lfCount - $crlfCount
$bom = $workerBytes.Length -ge 3 -and $workerBytes[0] -eq 239 -and $workerBytes[1] -eq 187 -and $workerBytes[2] -eq 191
Write-Host "START_PY_CRLF_COUNT=$crlfCount"
Write-Host "START_PY_LONE_LF_COUNT=$loneLfCount"
Write-Host "START_PY_LONE_CR_COUNT=$loneCrCount"
Write-Host "START_PY_BOM=$bom"
Write-Host "START_PY_NUL=$nul"
if ($loneLfCount -ne 0 -or $loneCrCount -ne 0 -or $bom -or $nul -or -not $ascii) { throw "Start.py encoding or line ending contract failed." }
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
    $worker -notmatch 'SELF_TEST_EXPECTED_HASH' -or
    $worker -notmatch 'F2B14D4200E1AC239FBF1CFD28D2F994' -or
    $worker -notmatch '39E631EC2D6FA129ECB6A92A841B75F2') {
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
if ($worker -match '(?m)^\s*(?:return\s+)?[^#\r\n:]+?\s+if\s+[^:\r\n]+?\s+else\s+[^#\r\n]+$') { throw "Python conditional expression found." }
if ($worker -match '\.sort\(key=' -or $worker -match '\.sort\(reverse=' -or $worker -match '\bsorted\(') { throw "Python 2.3-incompatible sort syntax found." }
if ($worker -match 'traceback\.format_exc\(' -or $worker -notmatch 'def _format_current_exception\(' -or $worker -notmatch 'sys\.exc_info\(\)' -or $worker -notmatch 'traceback\.format_exception\(') { throw "Python 2.3 traceback compatibility contract failed." }
if ($worker -match '0xffffffff' -or $worker -notmatch '_U32_MASK = 4294967295' -or $worker -notmatch 'def _u32\(' -or $worker -notmatch 'def _normalize_u32_values\(') { throw "SHA-256 unsigned 32-bit normalization contract failed." }
if ($worker -notmatch 'def _is_sha256_hex\(' -or $worker -notmatch 'SHA256_FALLBACK_SELF_TEST_START' -or $worker -notmatch 'PLAN_HASH_VALIDATE_START' -or $worker -notmatch 'PLAN_HASH_VALIDATE_FAILED') { throw "SHA-256 fail-closed contract is incomplete." }
foreach ($marker in @('PLAN_HASH_SORT_START','PLAN_HASH_SORT_RETURNED','READY_IDS_SORT_START','READY_IDS_SORT_RETURNED','CONFIRMED_IDS_SORT_START','CONFIRMED_IDS_SORT_RETURNED','CURRENT_IDS_SORT_START','CURRENT_IDS_SORT_RETURNED','FAILURE_RESULT_BUILD_START','FAILURE_RESULT_BUILD_RETURNED','FAILURE_RESULT_WRITE_FAILED','FAILURE_ARCHIVE_FAILED','PROCESS_FAILED')) {
    if ($worker -notmatch ('"' + $marker + '"')) { throw "Missing failure/sort marker: $marker" }
}
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
