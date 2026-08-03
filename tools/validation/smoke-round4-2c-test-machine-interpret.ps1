param([string]$BridgeRoot = "C:\AM_TribonBridge")
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$exe = Join-Path $BridgeRoot "console\AM.TribonAutomationProbe.Console.exe"
if (-not (Test-Path $exe)) { throw "Console not found" }
foreach ($name in "ASSISTANT_BASE_URL","ASSISTANT_API_KEY","ASSISTANT_MODEL") { if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { throw "Missing $name" } }
$before = @(Get-ChildItem (Join-Path $BridgeRoot "inbox") -Filter "*.request.json" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
$records = @(& $exe "assistant-interpret" "--text=识别当前图纸中的目标对象" 2>&1); $exit = $LASTEXITCODE; $output = ($records | ForEach-Object ToString | Out-String).Trim(); if ($exit -ne 0) { throw $output }
$result = $output | ConvertFrom-Json; if ($result.interpretation.provider -ne "openai-compatible-chat") { throw "Provider mismatch" }; if (@($result.interpretation.tasks).Count -ne 1 -or $result.interpretation.tasks[0].intent -ne "DetectGeometry") { throw "Intent mismatch" }; if ($result.plan.tasks[0].taskType -ne "geometry.detect") { throw "TaskType mismatch" }; if ($result.executionPerformed -ne $false -or $result.drawingWritePerformed -ne $false -or $result.savePerformed -ne $false -or $result.plan.autoSave -ne $false) { throw "Safety flags mismatch" }
$after = @(Get-ChildItem (Join-Path $BridgeRoot "inbox") -Filter "*.request.json" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name); if (@(Compare-Object $before $after).Count -ne 0) { throw "Interpretation created a FileBridge request" }
$path = Join-Path $BridgeRoot "diagnostics\round4-2c-test-machine-interpret.txt"; @("provider=$($result.interpretation.provider)","intent=DetectGeometry","taskType=geometry.detect","executionPerformed=False","drawingWritePerformed=False","savePerformed=False","autoSave=False","APIKeyRecorded=False","FileBridgeRequestPerformed=False","STATUS SUCCESS") | Set-Content $path
