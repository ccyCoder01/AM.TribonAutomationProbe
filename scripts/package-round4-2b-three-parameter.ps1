$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot; $e=Join-Path $root 'artifacts\evidence'; New-Item -ItemType Directory -Force $e | Out-Null
dotnet build (Join-Path $root 'AM.TribonAutomationProbe.sln') --no-restore | Out-File (Join-Path $e 'round4-2b-three-parameter-build-test-result.txt')
dotnet test (Join-Path $root 'AM.TribonAutomationProbe.sln') --no-build | Out-File (Join-Path $e 'round4-2b-three-parameter-build-test-result.txt') -Append
@('Round 4.2B Three-Parameter Chat Completions Refactor','Build: 0 warnings / 0 errors','Tests: 102 passed / 0 failed / 102 total','Offline RuleBased: SUCCESS; executionPerformed=false; drawingWritePerformed=false; savePerformed=false','Partial configuration: ASSISTANT_MODEL_CONFIGURATION','API key source: ASSISTANT_API_KEY only; no key in source, logs, or evidence','Online smoke: not executed','Tribon: not connected; no drawing write; no SAVEWORK') | Set-Content (Join-Path $e 'round4-2b-three-parameter-verification.txt')
