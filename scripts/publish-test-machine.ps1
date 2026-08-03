$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\AM.TribonAutomationProbe.Console\AM.TribonAutomationProbe.Console.csproj"
$output = Join-Path $root "artifacts\test-machine\win-x64"
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained true -o $output
$files = @(Get-ChildItem -LiteralPath $output -Recurse -File)
$bytes = ($files | Measure-Object -Property Length -Sum).Sum
Write-Host "Publish directory: $([IO.Path]::GetFullPath($output))"
Write-Host "Main exe: $([IO.Path]::GetFullPath((Join-Path $output 'AM.TribonAutomationProbe.Console.exe')))"
Write-Host "File count: $($files.Count)"
Write-Host "Total size: $bytes bytes"
