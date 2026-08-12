param(
    [Parameter(Mandatory=$true)]
    [string]$BridgeRoot,

    [Parameter(Mandatory=$true)]
    [string]$SessionId,

    [Parameter(Mandatory=$true)]
    [int]$DraftingPid,

    [Parameter(Mandatory=$true)]
    [int]$FunctionId,

    [Parameter(Mandatory=$true)]
    [double]$ReadyAt,

    [int]$ProofTimeoutSeconds = 900,

    [int]$WorkerResultTimeoutSeconds = 120,

    [ValidateSet("Supervisor","ResidentWorker")]
    [string]$Role = "Supervisor",

    [int]$MaxSilentRestarts = 3
)


Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

function Get-UnixSeconds-M {
    $epoch =
        [DateTime]::SpecifyKind(
            [DateTime]"1970-01-01T00:00:00",
            [DateTimeKind]::Utc
        )

    return (
        (
            [DateTime]::UtcNow -
            $epoch
        ).TotalSeconds
    )
}

function Write-AtomicText-M(
    [string]$Path,
    [string[]]$Lines
) {
    $temp = $Path + "." + [string]$PID + "." + [Guid]::NewGuid().ToString("N") + ".tmp"
    $replaceBackup = $null

    try {
        $utf8Bom = New-Object System.Text.UTF8Encoding($true)
        [System.IO.File]::WriteAllLines($temp, $Lines, $utf8Bom)

        $attempt = 0
        while ($true) {
            try {
                if ([System.IO.File]::Exists($Path)) {
                    $replaceBackup = $Path + "." + [string]$PID + "." + [Guid]::NewGuid().ToString("N") + ".replace.bak"
                    [System.IO.File]::Replace($temp, $Path, $replaceBackup)
                }
                else {
                    [System.IO.File]::Move($temp, $Path)
                }

                break
            }
            catch [System.IO.IOException] {
                $attempt++
                if ($attempt -ge 5) {
                    throw
                }

                Start-Sleep -Milliseconds (20 * $attempt)
            }
            finally {
                if ($replaceBackup -and (Test-Path -LiteralPath $replaceBackup)) {
                    Remove-Item -LiteralPath $replaceBackup -Force -ErrorAction SilentlyContinue
                }
                $replaceBackup = $null
            }
        }
    }
    finally {
        if (Test-Path -LiteralPath $temp) {
            Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
        }
        if ($replaceBackup -and (Test-Path -LiteralPath $replaceBackup)) {
            Remove-Item -LiteralPath $replaceBackup -Force -ErrorAction SilentlyContinue
        }
    }
}
function Get-KeyValueMap-M([string]$Path) {
    $map =
        @{}

    if (
        -not (
            Test-Path `
                -LiteralPath $Path `
                -PathType Leaf
        )
    ) {
        return $map
    }

    foreach ($line in @(
        Get-Content `
            -LiteralPath $Path `
            -ErrorAction SilentlyContinue
    )) {
        $index =
            $line.IndexOf("=")

        if ($index -lt 1) {
            continue
        }

        $map[
            $line.Substring(
                0,
                $index
            )
        ] =
            $line.Substring(
                $index + 1
            )
    }

    return $map
}

function Write-SupervisorHealth(
    [string]$Status,
    [int]$SupervisorPid,
    [int]$ChildPid,
    [int]$RestartCount,
    [double]$StartedAt,
    [double]$HeartbeatAt,
    [string]$LastExitClass,
    [string]$LastError
) {
    $gate =
        "PASS"

    if (
        $Status -like "FAULTED*"
    ) {
        $gate =
            "FAIL"
    }

    Write-AtomicText-M `
        $script:SupervisorHealthPath `
        @(
            "FORMAT=ROUND5_1E_O_WATCHER_SUPERVISOR_HEALTH_V1",
            "SUPERVISOR_PID=$SupervisorPid",
            "SUPERVISOR_STARTED_AT=$StartedAt",
            "HEARTBEAT_AT=$HeartbeatAt",
            "SESSION_ID=$SessionId",
            "DRAFTING_PID=$DraftingPid",
            "FUNCTION_ID=$FunctionId",
            "READY_AT=$ReadyAt",
            "CHILD_WATCHER_PID=$ChildPid",
            "SILENT_RESTART_COUNT=$RestartCount",
            "MAX_SILENT_RESTARTS=$MaxSilentRestarts",
            "STATUS=$Status",
            "LAST_EXIT_CLASS=$LastExitClass",
            "LAST_ERROR=$LastError",
            "DRAWING_WRITE_ALLOWED=False",
            "SAVEWORK_ALLOWED=False",
            "ROUND5_1E_O_WATCHER_SUPERVISOR_HEALTH_V1=$gate"
        )
}

function Start-ResidentChild {
    $arguments =
        @(
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-WindowStyle",
            "Hidden",
            "-File",
            $PSCommandPath,
            "-BridgeRoot",
            $BridgeRoot,
            "-SessionId",
            $SessionId,
            "-DraftingPid",
            [string]$DraftingPid,
            "-FunctionId",
            [string]$FunctionId,
            "-ReadyAt",
            [string]$ReadyAt,
            "-ProofTimeoutSeconds",
            [string]$ProofTimeoutSeconds,
            "-WorkerResultTimeoutSeconds",
            [string]$WorkerResultTimeoutSeconds,
            "-Role",
            "ResidentWorker",
            "-MaxSilentRestarts",
            [string]$MaxSilentRestarts
        )

    $process =
        Start-Process `
            -FilePath "powershell.exe" `
            -ArgumentList $arguments `
            -WindowStyle Hidden `
            -PassThru

    return $process
}

$BridgeRoot =
    [IO.Path]::GetFullPath(
        $BridgeRoot
    )

$diagnostics =
    Join-Path `
        $BridgeRoot `
        "diagnostics"

$script:SupervisorHealthPath =
    Join-Path `
        $diagnostics `
        "round5-1e-m-watcher-supervisor-health.latest.txt"

$ResidentHealthPath =
    Join-Path `
        $diagnostics `
        "round5-1e-l-resident-watcher-health.latest.txt"

if ($Role -eq "Supervisor") {
    $startedAt =
        Get-UnixSeconds-M

    $restartCount =
        0

    $child =
        $null

    $lastExitClass =
        ""

    Write-SupervisorHealth `
        "STARTING" `
        $PID `
        0 `
        $restartCount `
        $startedAt `
        $startedAt `
        "" `
        ""

    try {
        while ($true) {
            $draft =
                Get-Process `
                    -Id $DraftingPid `
                    -ErrorAction SilentlyContinue

            if ($null -eq $draft) {
                if (
                    $null -ne $child -and
                    -not $child.HasExited
                ) {
                    Stop-Process `
                        -Id $child.Id `
                        -Force `
                        -ErrorAction SilentlyContinue
                }

                Write-SupervisorHealth `
                    "STOPPED_DRAFTING_EXIT" `
                    $PID `
                    0 `
                    $restartCount `
                    $startedAt `
                    (Get-UnixSeconds-M) `
                    "DRAFTING_EXIT" `
                    ""

                exit 0
            }

            if ($null -eq $child) {
                $child =
                    Start-ResidentChild

                Start-Sleep -Milliseconds 300

                Write-SupervisorHealth `
                    "READY" `
                    $PID `
                    $child.Id `
                    $restartCount `
                    $startedAt `
                    (Get-UnixSeconds-M) `
                    $lastExitClass `
                    ""
            }
            elseif ($child.HasExited) {
                $residentHealth =
                    Get-KeyValueMap-M $ResidentHealthPath

                $residentFaulted =
                    $false

                if (
                    $residentHealth.ContainsKey("SESSION_ID") -and
                    $residentHealth["SESSION_ID"] -eq $SessionId -and
                    $residentHealth.ContainsKey("STATUS") -and
                    $residentHealth["STATUS"] -eq "FAULTED"
                ) {
                    $residentFaulted =
                        $true
                }

                if ($residentFaulted) {
                    $faultText =
                        ""

                    if (
                        $residentHealth.ContainsKey("LAST_ERROR")
                    ) {
                        $faultText =
                            [string]$residentHealth["LAST_ERROR"]
                    }

                    Write-SupervisorHealth `
                        "FAULTED_CHILD_SAFETY" `
                        $PID `
                        0 `
                        $restartCount `
                        $startedAt `
                        (Get-UnixSeconds-M) `
                        "CHILD_REPORTED_FAULTED" `
                        $faultText

                    exit 2
                }

                $restartCount++

                if (
                    $restartCount -gt
                        $MaxSilentRestarts
                ) {
                    Write-SupervisorHealth `
                        "FAULTED_RESTART_LIMIT" `
                        $PID `
                        0 `
                        $restartCount `
                        $startedAt `
                        (Get-UnixSeconds-M) `
                        "SILENT_EXIT_RESTART_LIMIT" `
                        "Resident watcher exceeded silent restart limit."

                    exit 3
                }

                $lastExitClass =
                    "SILENT_CHILD_EXIT"

                Start-Sleep -Milliseconds 500

                $child =
                    Start-ResidentChild

                Start-Sleep -Milliseconds 300

                Write-SupervisorHealth `
                    "READY" `
                    $PID `
                    $child.Id `
                    $restartCount `
                    $startedAt `
                    (Get-UnixSeconds-M) `
                    $lastExitClass `
                    ""
            }
            else {
                Write-SupervisorHealth `
                    "READY" `
                    $PID `
                    $child.Id `
                    $restartCount `
                    $startedAt `
                    (Get-UnixSeconds-M) `
                    $lastExitClass `
                    ""
            }

            Start-Sleep -Milliseconds 500
        }
    }
    catch {
        $message =
            [string]$_.Exception.Message

        Write-SupervisorHealth `
            "FAULTED_SUPERVISOR" `
            $PID `
            0 `
            $restartCount `
            $startedAt `
            (Get-UnixSeconds-M) `
            "SUPERVISOR_EXCEPTION" `
            $message

        throw
    }
}

# ResidentWorker role continues below with the proven L V1R1 worker body.

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$ReadOnlyActions =
    @(
        "geometry.detect",
        "geometry.highlight-lifting",
        "geometry.highlight-flanges",
        "geometry.highlight-clear",
        "geometry.label-preflight"
    )

$AuthorizedWriteActions =
    @(
        "geometry.label-apply-missing"
    )

function Get-UnixSeconds {
    $epoch =
        [DateTime]::SpecifyKind(
            [DateTime]"1970-01-01T00:00:00",
            [DateTimeKind]::Utc
        )

    return (
        (
            [DateTime]::UtcNow -
            $epoch
        ).TotalSeconds
    )
}

function Write-AtomicText(
    [string]$Path,
    [string[]]$Lines
) {
    $temp = $Path + "." + [string]$PID + "." + [Guid]::NewGuid().ToString("N") + ".tmp"
    $replaceBackup = $null

    try {
        $utf8Bom = New-Object System.Text.UTF8Encoding($true)
        [System.IO.File]::WriteAllLines($temp, $Lines, $utf8Bom)

        $attempt = 0
        while ($true) {
            try {
                if ([System.IO.File]::Exists($Path)) {
                    $replaceBackup = $Path + "." + [string]$PID + "." + [Guid]::NewGuid().ToString("N") + ".replace.bak"
                    [System.IO.File]::Replace($temp, $Path, $replaceBackup)
                }
                else {
                    [System.IO.File]::Move($temp, $Path)
                }

                break
            }
            catch [System.IO.IOException] {
                $attempt++
                if ($attempt -ge 5) {
                    throw
                }

                Start-Sleep -Milliseconds (20 * $attempt)
            }
            finally {
                if ($replaceBackup -and (Test-Path -LiteralPath $replaceBackup)) {
                    Remove-Item -LiteralPath $replaceBackup -Force -ErrorAction SilentlyContinue
                }
                $replaceBackup = $null
            }
        }
    }
    finally {
        if (Test-Path -LiteralPath $temp) {
            Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
        }
        if ($replaceBackup -and (Test-Path -LiteralPath $replaceBackup)) {
            Remove-Item -LiteralPath $replaceBackup -Force -ErrorAction SilentlyContinue
        }
    }
}
function Get-RequestFiles(
    [string]$InboxPath
) {
    return @(
        Get-ChildItem `
            -LiteralPath $InboxPath `
            -Filter "*.request.json" `
            -File `
            -ErrorAction SilentlyContinue |
        Sort-Object Name
    )
}

function Get-FileCount(
    [string]$Path,
    [string]$Filter
) {
    return @(
        Get-ChildItem `
            -LiteralPath $Path `
            -Filter $Filter `
            -File `
            -ErrorAction SilentlyContinue
    ).Count
}

function Test-TruthyWriteFlag(
    [string]$Raw
) {
    if (
        $Raw -match
            '(?i)"allowWrite"\s*:\s*true'
    ) {
        return $true
    }

    if (
        $Raw -match
            '(?i)"writeConfirmed"\s*:\s*true'
    ) {
        return $true
    }

    return $false
}


function Has-JsonProperty(
    [object]$Object,
    [string]$Name
) {
    if ($null -eq $Object) {
        return $false
    }

    return (
        $null -ne
        $Object.PSObject.Properties[$Name]
    )
}

function Get-StringArray(
    [object]$Value
) {
    return @(
        $Value |
        ForEach-Object {
            [string]$_
        }
    )
}

function Assert-StringSetEqual(
    [string]$Name,
    [string[]]$Actual,
    [string[]]$Expected
) {
    $actualSorted =
        @(
            $Actual |
            Sort-Object
        )

    $expectedSorted =
        @(
            $Expected |
            Sort-Object
        )

    if (
        $actualSorted.Count -ne
            $expectedSorted.Count
    ) {
        throw "$Name count mismatch."
    }

    for (
        $index = 0;
        $index -lt $actualSorted.Count;
        $index++
    ) {
        if (
            $actualSorted[$index] -cne
                $expectedSorted[$index]
        ) {
            throw "$Name content mismatch."
        }
    }

    if (
        @(
            $Actual |
            Sort-Object -Unique
        ).Count -ne
            $Actual.Count
    ) {
        throw "$Name contains duplicates."
    }
}

function Get-AuthorizedApplyBinding(
    [object]$Command,
    [string]$RequestRaw,
    [string]$LastAction,
    [string]$LastCommandId,
    [string]$OutputPath
) {
    if (
        $LastAction -ne
            "geometry.label-preflight" -or
        [string]::IsNullOrWhiteSpace(
            $LastCommandId
        )
    ) {
        throw "Authorized Apply requires the immediately preceding successful resident action to be geometry.label-preflight."
    }

    if (
        -not (
            Has-JsonProperty `
                $Command `
                "payload"
        )
    ) {
        throw "Authorized Apply payload is missing."
    }

    $payload =
        $Command.payload

    if (
        -not (
            Has-JsonProperty `
                $payload `
                "taskType"
        ) -or
        [string]$payload.taskType -ne
            "geometry.label-apply-missing"
    ) {
        throw "Authorized Apply taskType is invalid."
    }

    if (
        -not (
            Has-JsonProperty `
                $payload `
                "operationId"
        ) -or
        [string]::IsNullOrWhiteSpace(
            [string]$payload.operationId
        )
    ) {
        throw "Authorized Apply operationId is missing."
    }

    if (
        -not (
            Has-JsonProperty `
                $payload `
                "allowWrite"
        ) -or
        -not [bool]$payload.allowWrite
    ) {
        throw "Authorized Apply requires allowWrite=true."
    }

    if (
        -not (
            Has-JsonProperty `
                $payload `
                "writeConfirmed"
        ) -or
        -not [bool]$payload.writeConfirmed
    ) {
        throw "Authorized Apply requires writeConfirmed=true."
    }

    if (
        -not (
            Has-JsonProperty `
                $payload `
                "confirmedPreflightOperationId"
        )
    ) {
        throw "Authorized Apply confirmedPreflightOperationId is missing."
    }

    $confirmedPreflightOperationId =
        [string]$payload.confirmedPreflightOperationId

    if (
        [string]::IsNullOrWhiteSpace(
            $confirmedPreflightOperationId
        )
    ) {
        throw "Authorized Apply confirmedPreflightOperationId is blank."
    }

    if (
        -not (
            Has-JsonProperty `
                $payload `
                "confirmedPlanHash"
        )
    ) {
        throw "Authorized Apply confirmedPlanHash is missing."
    }

    $confirmedPlanHash =
        [string]$payload.confirmedPlanHash

    if (
        $confirmedPlanHash -notmatch
            '^[0-9A-Fa-f]{64}$'
    ) {
        throw "Authorized Apply confirmedPlanHash is invalid."
    }

    if (
        -not (
            Has-JsonProperty `
                $payload `
                "confirmedOperationIds"
        )
    ) {
        throw "Authorized Apply confirmedOperationIds is missing."
    }

    $confirmedOperationIds =
        @(
            Get-StringArray `
                $payload.confirmedOperationIds
        )

    if ($confirmedOperationIds.Count -eq 0) {
        throw "Authorized Apply confirmedOperationIds is empty."
    }

    foreach ($operationId in $confirmedOperationIds) {
        if (
            [string]::IsNullOrWhiteSpace(
                $operationId
            )
        ) {
            throw "Authorized Apply confirmedOperationIds contains a blank value."
        }
    }

    if (
        $RequestRaw -match
            '(?i)"saveWork"\s*:\s*true' -or
        $RequestRaw -match
            '(?i)"autoSave"\s*:\s*true'
    ) {
        throw "Authorized Apply rejected automatic SAVEWORK."
    }

    $preflightResultPath =
        Join-Path `
            $OutputPath `
            ($LastCommandId + ".result.json")

    if (
        -not (
            Test-Path `
                -LiteralPath $preflightResultPath `
                -PathType Leaf
        )
    ) {
        throw "Bound preflight result is missing."
    }

    $preflightRaw =
        Get-Content `
            -LiteralPath $preflightResultPath `
            -Raw `
            -ErrorAction Stop

    $preflightEnvelope =
        $preflightRaw |
            ConvertFrom-Json `
                -ErrorAction Stop

    if (
        [string]$preflightEnvelope.protocol -ne
            "AM.TribonBridge" -or
        [string]$preflightEnvelope.version -ne
            "0.1" -or
        [string]$preflightEnvelope.messageType -ne
            "bridge.result" -or
        [string]$preflightEnvelope.commandId -ne
            $LastCommandId -or
        [string]$preflightEnvelope.status -ne
            "succeeded"
    ) {
        throw "Bound preflight result envelope is invalid."
    }

    $preflight =
        $preflightEnvelope.result

    if (
        [string]$preflight.taskType -ne
            "geometry.label-preflight" -or
        [string]$preflight.status -ne
            "SUCCESS"
    ) {
        throw "Bound preflight result is not a successful label preflight."
    }

    if (
        [bool]$preflight.drawingWritePerformed
    ) {
        throw "Bound preflight unexpectedly reports a drawing write."
    }

    if (
        [bool]$preflight.savePerformed
    ) {
        throw "Bound preflight unexpectedly reports SAVEWORK."
    }

    $preflightOperationId =
        [string]$preflight.operationId

    $preflightPlanHash =
        [string]$preflight.planHash

    $readyOperationIds =
        @(
            Get-StringArray `
                $preflight.readyOperationIds
        )

    if (
        $confirmedPreflightOperationId -cne
            $preflightOperationId
    ) {
        throw "Authorized Apply confirmedPreflightOperationId does not match the immediately preceding preflight."
    }

    if (
        $confirmedPlanHash.ToUpperInvariant() -cne
            $preflightPlanHash.ToUpperInvariant()
    ) {
        throw "Authorized Apply confirmedPlanHash does not match the immediately preceding preflight."
    }

    Assert-StringSetEqual `
        "Authorized Apply confirmed operation IDs" `
        $confirmedOperationIds `
        $readyOperationIds

    if (
        $readyOperationIds.Count -eq 0
    ) {
        throw "Authorized Apply cannot run when the bound preflight has no READY_TO_CREATE operations."
    }

    if (
        [int]$preflight.preMissingCount -ne
            $readyOperationIds.Count
    ) {
        throw "Bound preflight missing count does not match its READY_TO_CREATE operation set."
    }

    return [pscustomobject]@{
        PreflightCommandId =
            $LastCommandId

        PreflightOperationId =
            $preflightOperationId

        PlanHash =
            $preflightPlanHash

        OperationIds =
            $readyOperationIds

        ExpectedCreateCount =
            $readyOperationIds.Count
    }
}

function Validate-AuthorizedApplyResult(
    [object]$ResultEnvelope,
    [object]$Command,
    [object]$Binding
) {
    $apply =
        $ResultEnvelope.result

    if (
        [string]$apply.taskType -ne
            "geometry.label-apply-missing"
    ) {
        throw "Authorized Apply result taskType is invalid."
    }

    if (
        [string]$apply.operationId -cne
            [string]$Command.payload.operationId
    ) {
        throw "Authorized Apply result operationId does not match the request."
    }

    if (
        [string]$apply.status -ne
            "SUCCESS"
    ) {
        throw "Authorized Apply inner status is not SUCCESS."
    }

    if (
        [int]$apply.createdCount -ne
            [int]$Binding.ExpectedCreateCount
    ) {
        throw "Authorized Apply did not create the complete confirmed operation set."
    }

    if (
        [int]$apply.createFailedCount -ne 0
    ) {
        throw "Authorized Apply reported create failures."
    }

    if (
        -not [bool]$apply.drawingWritePerformed
    ) {
        throw "Authorized Apply expected drawingWritePerformed=true."
    }

    if (
        [int]$apply.drawingWriteCount -ne
            [int]$apply.createdCount
    ) {
        throw "Authorized Apply drawingWriteCount does not equal createdCount."
    }

    if (
        [bool]$apply.savePerformed
    ) {
        throw "Authorized Apply reported SAVEWORK."
    }

    if (
        [int]$apply.postMissingCount -ne 0 -or
        [int]$apply.postDuplicateCount -ne 0 -or
        [int]$apply.postCreatedPropertyErrorCount -ne 0 -or
        [int]$apply.postExistingMatchErrorCount -ne 0 -or
        [int]$apply.postInspectionErrorCount -ne 0
    ) {
        throw "Authorized Apply post-check reported an invalid state."
    }

    if (
        [bool]$apply.manualRecoveryRequired
    ) {
        throw "Authorized Apply requires manual recovery."
    }

    $createdOperationIds =
        @(
            Get-StringArray `
                $apply.createdOperationIds
        )

    $failedOperationIds =
        @(
            Get-StringArray `
                $apply.failedOperationIds
        )

    if ($failedOperationIds.Count -ne 0) {
        throw "Authorized Apply returned failedOperationIds."
    }

    Assert-StringSetEqual `
        "Authorized Apply created operation IDs" `
        $createdOperationIds `
        $Binding.OperationIds
}

function Get-RequestCreatedUnix(
    [object]$Command
) {
    $created =
        [DateTimeOffset]::Parse(
            [string]$Command.createdAt,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind
        )

    return (
        $created.ToUniversalTime() -
        [DateTimeOffset]"1970-01-01T00:00:00+00:00"
    ).TotalSeconds
}

function Write-Health(
    [string]$Status,
    [int]$DispatchCount,
    [double]$StartedAt,
    [double]$HeartbeatAt,
    [string]$LastCommandId,
    [string]$LastAction,
    [string]$LastError
) {
    $roundStatus =
        "PASS"

    if ($Status -eq "FAULTED") {
        $roundStatus =
            "FAIL"
    }

    Write-AtomicText `
        $script:healthPath `
        @(
            "FORMAT=ROUND5_1E_O_RESIDENT_WATCHER_HEALTH_V1",
            "WATCHER_PID=$PID",
            "WATCHER_STARTED_AT=$StartedAt",
            "HEARTBEAT_AT=$HeartbeatAt",
            "SESSION_ID=$SessionId",
            "DRAFTING_PID=$DraftingPid",
            "FUNCTION_ID=$FunctionId",
            "READY_AT=$ReadyAt",
            "LIFETIME_MODE=DRAFTING_SESSION",
            "LEGACY_PROOF_TIMEOUT_SECONDS_IGNORED=$ProofTimeoutSeconds",
            "WORKER_RESULT_TIMEOUT_SECONDS=$WorkerResultTimeoutSeconds",
            "READONLY_ACTIONS=$($ReadOnlyActions -join ',')",
            "AUTHORIZED_WRITE_ACTIONS=$($AuthorizedWriteActions -join ',')",
            "WRITE_POLICY=EXPLICIT_PREFLIGHT_BINDING_ONLY",
            "SAVEWORK_ALLOWED=False",
            "STATUS=$Status",
            "DISPATCH_COUNT=$DispatchCount",
            "LAST_COMMAND_ID=$LastCommandId",
            "LAST_ACTION=$LastAction",
            "LAST_ERROR=$LastError",
            "ROUND5_1E_O_RESIDENT_WATCHER_HEALTH_V1=$roundStatus"
        )
}

if (
    -not (
        "AMRound51ELResidentWatcherNative" -as [type]
    )
) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class AMRound51ELResidentWatcherNative
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(
        IntPtr hWnd,
        uint Msg,
        IntPtr wParam,
        IntPtr lParam
    );
}
"@
}

$BridgeRoot =
    [IO.Path]::GetFullPath(
        $BridgeRoot
    )

$diagnostics =
    Join-Path `
        $BridgeRoot `
        "diagnostics"

$inbox =
    Join-Path `
        $BridgeRoot `
        "inbox"

$processing =
    Join-Path `
        $BridgeRoot `
        "processing"

$output =
    Join-Path `
        $BridgeRoot `
        "output"

$script:healthPath =
    Join-Path `
        $diagnostics `
        "round5-1e-l-resident-watcher-health.latest.txt"

$lastRunPath =
    Join-Path `
        $diagnostics `
        "round5-1e-l-resident-watcher-last-run.txt"

$startedAt =
    Get-UnixSeconds

$lastHeartbeatAt =
    0.0

$dispatchCount =
    0

$lastCommandId =
    ""

$lastAction =
    ""

$seenCommandIds =
    @{}

Write-Health `
    "READY" `
    $dispatchCount `
    $startedAt `
    $startedAt `
    $lastCommandId `
    $lastAction `
    ""

try {
    while ($true) {
        $now =
            Get-UnixSeconds

        $draft =
            Get-Process `
                -Id $DraftingPid `
                -ErrorAction SilentlyContinue

        if ($null -eq $draft) {
            Write-Health `
                "STOPPED_DRAFTING_EXIT" `
                $dispatchCount `
                $startedAt `
                $now `
                $lastCommandId `
                $lastAction `
                ""

            exit 0
        }

        if (
            ($now - $lastHeartbeatAt) -ge
                2.0
        ) {
            Write-Health `
                "READY" `
                $dispatchCount `
                $startedAt `
                $now `
                $lastCommandId `
                $lastAction `
                ""

            $lastHeartbeatAt =
                $now
        }

        $requests =
            @(
                Get-RequestFiles $inbox
            )

        if ($requests.Count -eq 0) {
            Start-Sleep -Milliseconds 200
            continue
        }

        if ($requests.Count -gt 1) {
            throw "Multiple inbox requests are present; resident watcher fails closed."
        }

        $processingCount =
            Get-FileCount `
                $processing `
                "*.request.json"

        if ($processingCount -ne 0) {
            throw "Processing directory is not idle before resident dispatch."
        }

        $requestFile =
            $requests[0]

        $requestRaw =
            Get-Content `
                -LiteralPath $requestFile.FullName `
                -Raw `
                -ErrorAction Stop

        try {
            $command =
                $requestRaw |
                ConvertFrom-Json `
                    -ErrorAction Stop
        }
        catch {
            throw "Inbox request JSON is invalid."
        }

        if (
            [string]$command.protocol -ne
                "AM.TribonBridge" -or
            [string]$command.version -ne
                "0.1" -or
            [string]$command.messageType -ne
                "bridge.command"
        ) {
            throw "Inbox request envelope is invalid."
        }

        $commandId =
            [string]$command.commandId

        $messageId =
            [string]$command.messageId

        $correlationId =
            [string]$command.correlationId

        $action =
            [string]$command.action

        if (
            [string]::IsNullOrWhiteSpace(
                $commandId
            ) -or
            [string]::IsNullOrWhiteSpace(
                $messageId
            ) -or
            [string]::IsNullOrWhiteSpace(
                $correlationId
            )
        ) {
            throw "Inbox request identifiers are incomplete."
        }

        $actionPolicy =
            ""

        $authorizedWriteBinding =
            $null

        if (
            $ReadOnlyActions -contains
                $action
        ) {
            if (
                Test-TruthyWriteFlag $requestRaw
            ) {
                throw "Read-only resident watcher rejected a truthy write flag."
            }

            $actionPolicy =
                "READONLY"
        }
        elseif (
            $AuthorizedWriteActions -contains
                $action
        ) {
            $authorizedWriteBinding =
                Get-AuthorizedApplyBinding `
                    $command `
                    $requestRaw `
                    $lastAction `
                    $lastCommandId `
                    $output

            $actionPolicy =
                "AUTHORIZED_WRITE"
        }
        else {
            throw "Action is not registered for resident dispatch: $action"
        }

        $createdUnix =
            Get-RequestCreatedUnix $command

        if (
            $createdUnix -le
                $ReadyAt
        ) {
            throw "Resident watcher rejected a stale request from before the current Drafting session."
        }

        if (
            $seenCommandIds.ContainsKey(
                $commandId
            )
        ) {
            throw "Resident watcher rejected a duplicate commandId: $commandId"
        }

        $resultPath =
            Join-Path `
                $output `
                ($commandId + ".result.json")

        if (
            Test-Path `
                -LiteralPath $resultPath `
                -PathType Leaf
        ) {
            throw "Result already exists for commandId before dispatch: $commandId"
        }

        $draft =
            Get-Process `
                -Id $DraftingPid `
                -ErrorAction Stop

        $mainWindowHandle =
            [int64]$draft.MainWindowHandle

        if ($mainWindowHandle -eq 0) {
            throw "Drafting MainWindowHandle is zero."
        }

        $dispatchSequence =
            $dispatchCount + 1

        $dispatchAt =
            Get-UnixSeconds

        $WM_COMMAND =
            [uint32]0x0111

        $posted =
            [AMRound51ELResidentWatcherNative]::PostMessage(
                [IntPtr]$mainWindowHandle,
                $WM_COMMAND,
                [IntPtr]$FunctionId,
                [IntPtr]::Zero
            )

        if (-not $posted) {
            throw "PostMessage returned false."
        }

        $resultDeadline =
            (Get-Date).AddSeconds(
                $WorkerResultTimeoutSeconds
            )

        while (
            (Get-Date) -lt
                $resultDeadline -and
            -not (
                Test-Path `
                    -LiteralPath $resultPath `
                    -PathType Leaf
            )
        ) {
            $draft =
                Get-Process `
                    -Id $DraftingPid `
                    -ErrorAction SilentlyContinue

            if ($null -eq $draft) {
                throw "Drafting exited while waiting for Worker result."
            }

            Start-Sleep -Milliseconds 100
        }

        if (
            -not (
                Test-Path `
                    -LiteralPath $resultPath `
                    -PathType Leaf
            )
        ) {
            throw "Timed out waiting for Worker result."
        }

        $resultRaw =
            Get-Content `
                -LiteralPath $resultPath `
                -Raw `
                -ErrorAction Stop

        $result =
            $resultRaw |
            ConvertFrom-Json `
                -ErrorAction Stop

        if (
            [string]$result.protocol -ne
                "AM.TribonBridge" -or
            [string]$result.version -ne
                "0.1" -or
            [string]$result.messageType -ne
                "bridge.result"
        ) {
            throw "Worker result envelope is invalid."
        }

        if (
            [string]$result.commandId -ne
                $commandId -or
            [string]$result.correlationId -ne
                $correlationId -or
            [string]$result.causationId -ne
                $messageId
        ) {
            throw "Worker result correlation does not match the request."
        }

        if (
            [string]$result.status -ne
                "succeeded"
        ) {
            throw "Worker result status is not succeeded."
        }

        $drawingWriteDetected =
            $resultRaw -match
                '(?i)"drawingWritePerformed"\s*:\s*true'

        $saveDetected =
            $resultRaw -match
                '(?i)"savePerformed"\s*:\s*true'

        if ($saveDetected) {
            throw "Resident result reported savePerformed=true."
        }

        if (
            $actionPolicy -eq
                "READONLY"
        ) {
            if ($drawingWriteDetected) {
                throw "Read-only resident result reported drawingWritePerformed=true."
            }
        }
        elseif (
            $actionPolicy -eq
                "AUTHORIZED_WRITE"
        ) {
            Validate-AuthorizedApplyResult `
                $result `
                $command `
                $authorizedWriteBinding
        }
        else {
            throw "Resident result action policy is invalid."
        }

        $idleDeadline =
            (Get-Date).AddSeconds(
                10
            )

        while (
            (Get-Date) -lt
                $idleDeadline
        ) {
            $inboxCountAfter =
                Get-FileCount `
                    $inbox `
                    "*.request.json"

            $processingCountAfter =
                Get-FileCount `
                    $processing `
                    "*.request.json"

            if (
                $inboxCountAfter -eq 0 -and
                $processingCountAfter -eq 0
            ) {
                break
            }

            Start-Sleep -Milliseconds 100
        }

        if (
            (Get-FileCount $inbox "*.request.json") -ne 0 -or
            (Get-FileCount $processing "*.request.json") -ne 0
        ) {
            throw "FileBridge did not return to idle after Worker result."
        }

        $completedAt =
            Get-UnixSeconds

        $latencyMs =
            [Math]::Round(
                (
                    $completedAt -
                    $dispatchAt
                ) * 1000.0,
                3
            )

        $dispatchCount =
            $dispatchSequence

        $seenCommandIds[
            $commandId
        ] =
            $true

        $lastCommandId =
            $commandId

        $lastAction =
            $action

        $sequenceText =
            $dispatchSequence.ToString(
                "D6"
            )

        $dispatchReceiptPath =
            Join-Path `
                $diagnostics `
                (
                    "round5-1e-l-resident-watcher-dispatch-" +
                    $sequenceText +
                    ".txt"
                )

        $receiptLines =
            @(
                "FORMAT=ROUND5_1E_O_RESIDENT_WATCHER_DISPATCH_V1",
                "SESSION_ID=$SessionId",
                "WATCHER_PID=$PID",
                "DRAFTING_PID=$DraftingPid",
                "DRAFTING_MAIN_WINDOW_HANDLE=$mainWindowHandle",
                "FUNCTION_ID=$FunctionId",
                "DISPATCH_SEQUENCE=$dispatchSequence",
                "REQUEST_PATH=$($requestFile.FullName)",
                "ACTION=$action",
                "COMMAND_ID=$commandId",
                "CORRELATION_ID=$correlationId",
                "CAUSATION_SOURCE_MESSAGE_ID=$messageId",
                "REQUEST_CREATED_UNIX=$createdUnix",
                "READY_AT=$ReadyAt",
                "STALE_REQUEST_GATE=PASS",
                "ACTION_POLICY=$actionPolicy",
                "READONLY_ACTION_GATE=$(if ($actionPolicy -eq 'READONLY') { 'PASS' } else { 'N/A' })",
                "AUTHORIZED_WRITE_ACTION_GATE=$(if ($actionPolicy -eq 'AUTHORIZED_WRITE') { 'PASS' } else { 'N/A' })",
                "WRITE_FLAG_GATE=PASS",
                "BOUND_PREFLIGHT_COMMAND_ID=$(if ($null -ne $authorizedWriteBinding) { $authorizedWriteBinding.PreflightCommandId } else { '' })",
                "CONFIRMED_PREFLIGHT_OPERATION_ID=$(if ($null -ne $authorizedWriteBinding) { $authorizedWriteBinding.PreflightOperationId } else { '' })",
                "CONFIRMED_PLAN_HASH=$(if ($null -ne $authorizedWriteBinding) { $authorizedWriteBinding.PlanHash } else { '' })",
                "CONFIRMED_OPERATION_ID_COUNT=$(if ($null -ne $authorizedWriteBinding) { $authorizedWriteBinding.OperationIds.Count } else { 0 })",
                "EXPECTED_CREATE_COUNT=$(if ($null -ne $authorizedWriteBinding) { $authorizedWriteBinding.ExpectedCreateCount } else { 0 })",
                "AUTHORIZED_WRITE_BINDING_GATE=$(if ($actionPolicy -eq 'AUTHORIZED_WRITE') { 'PASS' } else { 'N/A' })",
                "SINGLE_REQUEST_GATE=PASS",
                "PROCESSING_IDLE_GATE=PASS",
                "DUPLICATE_COMMAND_GATE=PASS",
                "DISPATCH_AT=$dispatchAt",
                "POSTMESSAGE_RETURNED=True",
                "RESULT_PATH=$resultPath",
                "RESULT_STATUS=succeeded",
                "RESULT_CORRELATION_GATE=PASS",
                "DRAWING_WRITE_DETECTED=$drawingWriteDetected",
                "SAVE_DETECTED=$saveDetected",
                "AUTHORIZED_WRITE_POSTCHECK_GATE=$(if ($actionPolicy -eq 'AUTHORIZED_WRITE') { 'PASS' } else { 'N/A' })",
                "FILEBRIDGE_IDLE_AFTER_RESULT=True",
                "DISPATCH_TO_RESULT_LATENCY_MS=$latencyMs",
                "ROUND5_1E_O_RESIDENT_WATCHER_DISPATCH_V1=PASS"
            )

        Write-AtomicText `
            $dispatchReceiptPath `
            $receiptLines

        Write-AtomicText `
            $lastRunPath `
            $receiptLines

        Write-Health `
            "READY" `
            $dispatchCount `
            $startedAt `
            $completedAt `
            $lastCommandId `
            $lastAction `
            ""

        $lastHeartbeatAt =
            $completedAt
    }
}
catch {
    $errorText =
        [string]$_.Exception.Message

    try {
        Write-Health `
            "FAULTED" `
            $dispatchCount `
            $startedAt `
            (Get-UnixSeconds) `
            $lastCommandId `
            $lastAction `
            $errorText
    }
    catch {
    }

    throw
}
