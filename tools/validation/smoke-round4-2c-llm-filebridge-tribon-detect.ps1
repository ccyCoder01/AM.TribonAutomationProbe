param([string]$BridgeRoot = "C:\AM_TribonBridge", [int]$TimeoutSeconds = 600)
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$exe = Join-Path $BridgeRoot "console\AM.TribonAutomationProbe.Console.exe"; if(-not(Test-Path $exe)){throw "Console not found"}
foreach($name in "ASSISTANT_BASE_URL","ASSISTANT_API_KEY","ASSISTANT_MODEL"){if([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))){throw "Missing $name"}}
Write-Host "WAITING_FOR_TRIBON"; Write-Host "TaskType : geometry.detect"; Write-Host "Action   : Run Start.py exactly once in Tribon."
$records=@(& $exe "assistant-run" "--text=识别当前图纸中的目标对象" "--adapter=file-bridge" "--bridge-root=$BridgeRoot" "--execution-profile=round4-2c-readonly" "--execute=true" "--timeout-ms=$($TimeoutSeconds*1000)" 2>&1); $exit=$LASTEXITCODE; $out=($records|ForEach-Object ToString|Out-String).Trim(); if($exit -ne 0){throw $out}; $r=$out|ConvertFrom-Json
if($r.status -ne "SUCCESS" -or @($r.interpretation.tasks).Count -ne 1 -or $r.interpretation.tasks[0].intent -ne "DetectGeometry" -or $r.plan.tasks[0].taskType -ne "geometry.detect" -or $r.executionPerformed -ne $true -or $r.drawingWritePerformed -ne $false -or $r.savePerformed -ne $false -or $r.plan.autoSave -ne $false){throw "Detect result safety or task mismatch"}
$path=Join-Path $BridgeRoot "diagnostics\round4-2c-llm-filebridge-tribon-detect.txt"; @("Intent=DetectGeometry","TaskType=geometry.detect","ExecutionPerformed=True","DrawingWritePerformed=False","SavePerformed=False","AutoSave=False","APIKeyRecorded=False","STATUS=SUCCESS")|Set-Content $path

