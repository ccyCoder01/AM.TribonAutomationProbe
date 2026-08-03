[CmdletBinding()]
param(
    [string]$Root = "D:\CodeNetSpace\AM.TribonAutomationProbe",
    [string]$Text = "先识别当前图纸中的目标对象，然后高亮所有法兰"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-Configured {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [AllowNull()]
        [AllowEmptyString()]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw ("Required environment variable is not configured: {0}" -f $Name)
    }
}

function Get-OptionalPropertyValue {
    param(
        [AllowNull()]
        [object]$InputObject,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ($null -eq $InputObject) {
        return $null
    }

    $property = $InputObject.PSObject.Properties[$Name]

    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

Assert-Configured -Name "ASSISTANT_BASE_URL" -Value $env:ASSISTANT_BASE_URL
Assert-Configured -Name "ASSISTANT_API_KEY" -Value $env:ASSISTANT_API_KEY
Assert-Configured -Name "ASSISTANT_MODEL" -Value $env:ASSISTANT_MODEL

$Root = (Resolve-Path -LiteralPath $Root).Path

$ConsoleExe = Join-Path `
    $Root `
    "src\AM.TribonAutomationProbe.Console\bin\Debug\net8.0\AM.TribonAutomationProbe.Console.exe"

$EvidenceDirectory = Join-Path $Root "artifacts\evidence"
$EvidencePath = Join-Path `
    $EvidenceDirectory `
    "round4-2b-chat-completions-online-smoke.txt"

if (-not (Test-Path -LiteralPath $ConsoleExe -PathType Leaf)) {
    throw ("Console executable not found: {0}" -f $ConsoleExe)
}

Write-Host "This smoke test performs one Chat Completions API request."
Write-Host "It uses assistant-interpret only."
Write-Host "It does not call FileBridge or Tribon, modify the drawing, or execute SAVEWORK."

$previousErrorActionPreference = $ErrorActionPreference

try {
    # Windows PowerShell 5.1 can wrap native stderr as NativeCommandError.
    # Capture both streams and validate the native exit code explicitly.
    $ErrorActionPreference = "Continue"

    $records = @(
        & $ConsoleExe `
            "assistant-interpret" `
            ("--text={0}" -f $Text) 2>&1
    )

    $exitCode = $LASTEXITCODE
    $output = (
        $records |
            ForEach-Object { $_.ToString() } |
            Out-String
    ).Trim()
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}

if ($exitCode -ne 0) {
    throw (
        "Online smoke test failed with exit code {0}: {1}" -f `
            $exitCode, `
            $output
    )
}

try {
    $result = $output | ConvertFrom-Json
}
catch {
    throw (
        "Online smoke test did not return valid JSON: {0}" -f `
            $output
    )
}

$interpretation = Get-OptionalPropertyValue `
    -InputObject $result `
    -Name "interpretation"

$plan = Get-OptionalPropertyValue `
    -InputObject $result `
    -Name "plan"

if ($null -eq $interpretation) {
    throw "Online smoke result is missing interpretation."
}

if ($null -eq $plan) {
    throw "Online smoke result is missing plan."
}

$executionPerformed = Get-OptionalPropertyValue `
    -InputObject $result `
    -Name "executionPerformed"

$drawingWritePerformed = Get-OptionalPropertyValue `
    -InputObject $result `
    -Name "drawingWritePerformed"

$savePerformed = Get-OptionalPropertyValue `
    -InputObject $result `
    -Name "savePerformed"

if ($executionPerformed -ne $false) {
    throw (
        "assistant-interpret unexpectedly reported executionPerformed={0}." -f `
            $executionPerformed
    )
}

if ($drawingWritePerformed -ne $false) {
    throw (
        "assistant-interpret unexpectedly reported drawingWritePerformed={0}." -f `
            $drawingWritePerformed
    )
}

if ($savePerformed -ne $false) {
    throw (
        "assistant-interpret unexpectedly reported savePerformed={0}." -f `
            $savePerformed
    )
}

$autoSave = Get-OptionalPropertyValue `
    -InputObject $plan `
    -Name "autoSave"

if ($autoSave -ne $false) {
    throw (
        "Online smoke plan unexpectedly reported autoSave={0}." -f `
            $autoSave
    )
}

$provider = [string](
    Get-OptionalPropertyValue `
        -InputObject $interpretation `
        -Name "provider"
)

if ($provider -ne "openai-compatible-chat") {
    throw (
        "Expected provider openai-compatible-chat, actual: {0}" -f `
            $provider
    )
}

$model = [string](
    Get-OptionalPropertyValue `
        -InputObject $interpretation `
        -Name "model"
)

$requestId = [string](
    Get-OptionalPropertyValue `
        -InputObject $interpretation `
        -Name "requestId"
)

$responseId = [string](
    Get-OptionalPropertyValue `
        -InputObject $interpretation `
        -Name "responseId"
)

$latencyMs = Get-OptionalPropertyValue `
    -InputObject $interpretation `
    -Name "latencyMs"

$interpretedTasks = @(
    Get-OptionalPropertyValue `
        -InputObject $interpretation `
        -Name "tasks"
)

$plannedTasks = @(
    Get-OptionalPropertyValue `
        -InputObject $plan `
        -Name "tasks"
)

$intents = @(
    $interpretedTasks |
        ForEach-Object {
            [string](
                Get-OptionalPropertyValue `
                    -InputObject $_ `
                    -Name "intent"
            )
        }
)

$taskTypes = @(
    $plannedTasks |
        ForEach-Object {
            [string](
                Get-OptionalPropertyValue `
                    -InputObject $_ `
                    -Name "taskType"
            )
        }
)

$expectedIntents = @(
    "DetectGeometry",
    "HighlightFlanges"
)

$expectedTaskTypes = @(
    "geometry.detect",
    "geometry.highlight-flanges"
)

if ($intents.Count -ne $expectedIntents.Count) {
    throw (
        "Expected {0} interpreted tasks, actual {1}: {2}" -f `
            $expectedIntents.Count, `
            $intents.Count, `
            ($intents -join ",")
    )
}

for ($index = 0; $index -lt $expectedIntents.Count; $index++) {
    if ($intents[$index] -ne $expectedIntents[$index]) {
        throw (
            "Unexpected intent at index {0}. Expected={1} Actual={2}" -f `
                $index, `
                $expectedIntents[$index], `
                $intents[$index]
        )
    }
}

if ($taskTypes.Count -ne $expectedTaskTypes.Count) {
    throw (
        "Expected {0} planned tasks, actual {1}: {2}" -f `
            $expectedTaskTypes.Count, `
            $taskTypes.Count, `
            ($taskTypes -join ",")
    )
}

for ($index = 0; $index -lt $expectedTaskTypes.Count; $index++) {
    if ($taskTypes[$index] -ne $expectedTaskTypes[$index]) {
        throw (
            "Unexpected task type at index {0}. Expected={1} Actual={2}" -f `
                $index, `
                $expectedTaskTypes[$index], `
                $taskTypes[$index]
        )
    }
}

New-Item `
    -ItemType Directory `
    -Path $EvidenceDirectory `
    -Force |
    Out-Null

@(
    "FORMAT  AM_ROUND4_2B_CHAT_COMPLETIONS_ONLINE_SMOKE_V2",
    ("COMPLETED_AT    {0}" -f [DateTimeOffset]::Now.ToString("o")),
    ("BASE_URL    {0}" -f $env:ASSISTANT_BASE_URL),
    ("PROVIDER    {0}" -f $provider),
    ("MODEL    {0}" -f $model),
    ("REQUEST_ID    {0}" -f $requestId),
    ("RESPONSE_ID    {0}" -f $responseId),
    ("LATENCY_MS    {0}" -f $latencyMs),
    ("INTENTS    {0}" -f ($intents -join ",")),
    ("TASK_TYPES    {0}" -f ($taskTypes -join ",")),
    "EXECUTION_PERFORMED    False",
    "DRAWING_WRITE_PERFORMED    False",
    "SAVE_PERFORMED    False",
    "API_KEY_RECORDED    False",
    "STATUS    SUCCESS"
) | Set-Content -LiteralPath $EvidencePath -Encoding UTF8

Write-Host ""
Write-Host "===== ROUND 4.2B CHAT COMPLETIONS ONLINE SMOKE ====="
Write-Host ("BaseUrl           : {0}" -f $env:ASSISTANT_BASE_URL)
Write-Host ("Provider          : {0}" -f $provider)
Write-Host ("Model             : {0}" -f $model)
Write-Host ("RequestId         : {0}" -f $(if ([string]::IsNullOrWhiteSpace($requestId)) { "<not returned>" } else { $requestId }))
Write-Host ("ResponseId        : {0}" -f $(if ([string]::IsNullOrWhiteSpace($responseId)) { "<not returned>" } else { $responseId }))
Write-Host ("LatencyMs         : {0}" -f $latencyMs)
Write-Host ("Intents           : {0}" -f ($intents -join ", "))
Write-Host ("TaskTypes         : {0}" -f ($taskTypes -join ", "))
Write-Host "Execution         : NOT PERFORMED"
Write-Host "DrawingWrite      : NOT PERFORMED"
Write-Host "SAVEWORK          : NOT PERFORMED"
Write-Host "APIKeyRecorded    : False"
Write-Host ("Evidence          : {0}" -f $EvidencePath)
Write-Host "STATUS            : SUCCESS"
