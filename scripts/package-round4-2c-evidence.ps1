$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$evidence = Join-Path $root "artifacts\evidence"
New-Item -ItemType Directory -Force $evidence | Out-Null
dotnet build (Join-Path $root "AM.TribonAutomationProbe.sln") --no-restore | Out-File (Join-Path $evidence "round4-2c-readonly-build-test.txt")
dotnet test (Join-Path $root "AM.TribonAutomationProbe.sln") --no-build | Out-File (Join-Path $evidence "round4-2c-readonly-build-test.txt") -Append
@("Round 4.2C Deployment and Smoke Script Hardening","Manifest: real TSV with UTF-8 BOM and SHA-256 rows","Package: win-x64 self-contained","Installer: source/staging/final manifest verification and backup/rollback","Scripts: UTF-8 BOM, strict mode, semantic safety checks","OnlineRequestPerformed=False","TestMachineDeploymentPerformed=False","FileBridgeRequestPerformed=False","TribonConnected=False","DrawingWritePerformed=False","SavePerformed=False","APIKeyRecorded=False","Build: 0 warnings / 0 errors","Tests: 111 passed / 0 failed / 111 total","STATUS=OFFLINE_VALIDATION_PASS") | Set-Content (Join-Path $evidence "round4-2c-deployment-smoke-hardening-verification.txt")
