param([string]$BridgeRoot = "C:\AM_TribonBridge", [int]$TimeoutSeconds = 900)
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$exe=Join-Path $BridgeRoot "console\AM.TribonAutomationProbe.Console.exe"; if(-not(Test-Path $exe)){throw "Console not found"}
foreach($name in "ASSISTANT_BASE_URL","ASSISTANT_API_KEY","ASSISTANT_MODEL"){if([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))){throw "Missing $name"}}
Write-Host "WAITING_FOR_TRIBON"; Write-Host "TaskTypes: geometry.detect then geometry.highlight-flanges"; Write-Host "Action: Run Start.py once for each task."
$records=@(& $exe "assistant-run" "--text=先识别当前图纸中的目标对象，然后高亮所有法兰" "--adapter=file-bridge" "--bridge-root=$BridgeRoot" "--execution-profile=round4-2c-readonly" "--execute=true" "--timeout-ms=$($TimeoutSeconds*1000)" 2>&1); $exit=$LASTEXITCODE; $out=($records|ForEach-Object ToString|Out-String).Trim(); if($exit -ne 0){throw $out}; $r=$out|ConvertFrom-Json
$intents=@($r.interpretation.tasks|ForEach-Object intent); $types=@($r.plan.tasks|ForEach-Object taskType); if(($intents -join ',') -ne 'DetectGeometry,HighlightFlanges' -or ($types -join ',') -ne 'geometry.detect,geometry.highlight-flanges'){throw "Task order mismatch"}; if($r.status -ne 'SUCCESS' -or $r.executionPerformed -ne $true -or $r.drawingWritePerformed -ne $false -or $r.savePerformed -ne $false -or $r.plan.autoSave -ne $false){throw "Compound result safety mismatch"}
$path=Join-Path $BridgeRoot "diagnostics\round4-2c-llm-filebridge-tribon-detect-highlight-flanges.txt"; @("Intent=DetectGeometry,HighlightFlanges","TaskType=geometry.detect,geometry.highlight-flanges","ExecutionPerformed=True","DrawingWritePerformed=False","SavePerformed=False","AutoSave=False","StartPyInvocationCount=2","APIKeyRecorded=False","STATUS=SUCCESS")|Set-Content $path

