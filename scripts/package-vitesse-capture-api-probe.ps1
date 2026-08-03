$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root "vitesse\AddIns\AMCaptureApiProbe"
$files = @((Join-Path $source "__init__.py"), (Join-Path $source "Start.py"))
foreach ($file in $files) { if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing AddIn file: $file" } }
$output = Join-Path $root "artifacts\vitesse\AMCaptureApiProbe.zip"
$parent = Split-Path -Parent $output
if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent | Out-Null }
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }
$stage = Join-Path ([IO.Path]::GetTempPath()) ("AMCaptureApiProbe-" + [guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path (Join-Path $stage "AMCaptureApiProbe") -Force | Out-Null
Copy-Item -LiteralPath $files[0] -Destination (Join-Path $stage "AMCaptureApiProbe\__init__.py")
Copy-Item -LiteralPath $files[1] -Destination (Join-Path $stage "AMCaptureApiProbe\Start.py")
Compress-Archive -Path (Join-Path $stage "AMCaptureApiProbe") -DestinationPath $output
$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
Write-Host "ZIP: $([IO.Path]::GetFullPath($output))"
Write-Host "Size: $((Get-Item -LiteralPath $output).Length) bytes"
Write-Host "SHA256: $hash"
Remove-Item -LiteralPath $stage -Recurse -Force
