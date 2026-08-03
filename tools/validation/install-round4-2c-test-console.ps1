param(
    [string]$BridgeRoot = "C:\AM_TribonBridge",
    [string]$PackageRoot,
    [string]$ManifestPath
)
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$bundleRoot = Split-Path -Parent $PSScriptRoot
$repo = Split-Path -Parent $bundleRoot

if ([string]::IsNullOrWhiteSpace($PackageRoot)) {
    $bundlePackage = Join-Path $bundleRoot "package"
    $repoPackage = Join-Path $repo "artifacts\test-machine\win-x64"

    if (Test-Path -LiteralPath $bundlePackage -PathType Container) {
        $PackageRoot = $bundlePackage
    }
    elseif (Test-Path -LiteralPath $repoPackage -PathType Container) {
        $PackageRoot = $repoPackage
    }
    else {
        throw "Unable to locate console package."
    }
}

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $bundleManifest = Join-Path $bundleRoot "evidence\round4-2c-test-console-package-manifest.tsv"
    $repoManifest = Join-Path $repo "artifacts\evidence\round4-2c-test-console-package-manifest.tsv"

    if (Test-Path -LiteralPath $bundleManifest -PathType Leaf) {
        $ManifestPath = $bundleManifest
    }
    elseif (Test-Path -LiteralPath $repoManifest -PathType Leaf) {
        $ManifestPath = $repoManifest
    }
    else {
        throw "Unable to locate console package manifest."
    }
}
$PackageRoot = (Resolve-Path $PackageRoot).Path; $ManifestPath = (Resolve-Path $ManifestPath).Path
$diagnostics = Join-Path $BridgeRoot "diagnostics"; New-Item -ItemType Directory -Force $diagnostics | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"; $target = Join-Path $BridgeRoot "console"; $staging = Join-Path $BridgeRoot ("console.staging." + $stamp); $backup = Join-Path $BridgeRoot ("backups\round4-2c-console-" + $stamp + "\console")
function Read-Manifest($path) { $rows=Import-Csv -LiteralPath $path -Delimiter "`t"; if ((Get-Content $path -TotalCount 1) -ne "path`tlength`thash256" -and (Get-Content $path -TotalCount 1) -ne "path`tlength`tsha256") { throw "Invalid manifest header" }; return @($rows) }
function Test-Package($root,$rows) { $actual=@(Get-ChildItem $root -Recurse -File | ForEach-Object { $_.FullName.Substring($root.Length+1).Replace('\','/') }); $listed=@($rows.path); if (@(Compare-Object $actual $listed).Count -gt 0) { throw "Package files do not match manifest" }; foreach($row in $rows){ if($row.path -match '(^/|^[A-Za-z]:|\.\.)'){throw "Unsafe manifest path: $($row.path)"}; $file=Join-Path $root ($row.path.Replace('/', '\')); if(-not(Test-Path $file)){throw "Missing file: $($row.path)"}; if(([int64](Get-Item $file).Length) -ne [int64]$row.length){throw "Length mismatch: $($row.path)"}; if((Get-FileHash $file -Algorithm SHA256).Hash -ne $row.sha256){throw "Hash mismatch: $($row.path)"} } }
$rows=Read-Manifest $ManifestPath; Test-Package $PackageRoot $rows
if (@(Get-Process -Name "AM.TribonAutomationProbe.Console" -ErrorAction SilentlyContinue).Count -gt 0) { throw "Console process is running" }
New-Item -ItemType Directory -Force (Split-Path $backup) | Out-Null
if(Test-Path $target){ Copy-Item $target $backup -Recurse }
Copy-Item $PackageRoot $staging -Recurse; Test-Package $staging $rows
$help = Join-Path $staging "AM.TribonAutomationProbe.Console.exe"; & $help --help | Out-Null; $helpExit=$LASTEXITCODE; if($helpExit -ne 0){throw "Staged help failed"}
$rollback=$false; try { if(Test-Path $target){Rename-Item $target ($target + ".old." + $stamp)}; Rename-Item $staging $target; Test-Package $target $rows } catch { $rollback=$true; if(Test-Path $target){Remove-Item $target -Recurse -Force}; if(Test-Path $backup){Move-Item $backup $target}; throw }
@("packageRoot=$PackageRoot","manifestPath=$ManifestPath","manifestSha256=$((Get-FileHash $ManifestPath -Algorithm SHA256).Hash)","fileCount=$($rows.Count)","sourceVerification=PASS","stagingVerification=PASS","oldTargetBackedUp=$([bool](Test-Path $backup))","backupPath=$backup","helpExitCode=$helpExit","targetVerification=PASS","rollbackPerformed=$rollback","APIKeyRecorded=False","FileBridgeRequestPerformed=False","TribonConnected=False","DrawingWritePerformed=False","SavePerformed=False","STATUS=SUCCESS") | Set-Content (Join-Path $diagnostics "round4-2c-test-console-install.txt")




