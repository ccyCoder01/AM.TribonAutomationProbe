[CmdletBinding()]
param(
    [string]$RepositoryRoot = "",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent $PSScriptRoot
}

$RepositoryRoot = [System.IO.Path]::GetFullPath(
    $RepositoryRoot
)

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path `
        $RepositoryRoot `
        "artifacts\desktop\win-x64"
}

$OutputDirectory = [System.IO.Path]::GetFullPath(
    $OutputDirectory
)

$desktopProject = Join-Path `
    $RepositoryRoot `
    "src\AM.TribonAutomationProbe.Desktop\AM.TribonAutomationProbe.Desktop.csproj"
$consoleProject = Join-Path `
    $RepositoryRoot `
    "src\AM.TribonAutomationProbe.Console\AM.TribonAutomationProbe.Console.csproj"
$testProject = Join-Path `
    $RepositoryRoot `
    "tests\AM.TribonAutomationProbe.Desktop.Tests\AM.TribonAutomationProbe.Desktop.Tests.csproj"

foreach ($path in @(
    $desktopProject,
    $consoleProject,
    $testProject
)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required desktop publish project is missing: $path"
    }
}

$dotnetSdks = @(
    & dotnet --list-sdks 2>&1 |
    ForEach-Object {
        [string]$_
    }
)

if ($LASTEXITCODE -ne 0) {
    throw "dotnet CLI is unavailable."
}

$dotnet8SdkAvailable = @(
    $dotnetSdks |
    Where-Object {
        $_ -match '^8\.'
    }
).Count -gt 0

if (-not $dotnet8SdkAvailable) {
    throw ".NET 8 SDK is required to build the desktop workflow."
}

$stagingRoot = Join-Path `
    ([System.IO.Path]::GetTempPath()) `
    ("AM.TribonAutomationProbe.Desktop-" +
     (Get-Date -Format "yyyyMMdd-HHmmss") +
     "-" +
     $PID)
$desktopOutput = Join-Path $stagingRoot "desktop"
$consoleOutput = Join-Path $desktopOutput "console"
$manifestPath = Join-Path $desktopOutput "package-manifest.csv"
$zipPath = $OutputDirectory.TrimEnd('\') + ".zip"

Remove-Item `
    -LiteralPath $stagingRoot `
    -Recurse `
    -Force `
    -ErrorAction SilentlyContinue
Remove-Item `
    -LiteralPath $OutputDirectory `
    -Recurse `
    -Force `
    -ErrorAction SilentlyContinue
Remove-Item `
    -LiteralPath $zipPath `
    -Force `
    -ErrorAction SilentlyContinue

New-Item `
    -ItemType Directory `
    -Path $desktopOutput `
    -Force |
    Out-Null

try {
    Write-Host "===== RESTORE AND TEST ====="

    & dotnet restore $testProject

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed."
    }

    & dotnet test `
        $testProject `
        -c Release `
        --no-restore

    if ($LASTEXITCODE -ne 0) {
        throw "Desktop workflow tests failed."
    }

    Write-Host "===== PUBLISH VERIFIED CONSOLE ====="

    & dotnet publish `
        $consoleProject `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -o $consoleOutput

    if ($LASTEXITCODE -ne 0) {
        throw "Console self-contained publish failed."
    }

    Write-Host "===== PUBLISH DESKTOP SHELL ====="

    & dotnet publish `
        $desktopProject `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -o $desktopOutput

    if ($LASTEXITCODE -ne 0) {
        throw "Desktop self-contained publish failed."
    }

    $desktopExe = Join-Path `
        $desktopOutput `
        "AM.TribonAutomationProbe.Desktop.exe"
    $consoleExe = Join-Path `
        $consoleOutput `
        "AM.TribonAutomationProbe.Console.exe"

    foreach ($path in @(
        $desktopExe,
        $consoleExe
    )) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Expected published executable is missing: $path"
        }
    }

    $files = @(
        Get-ChildItem `
            -LiteralPath $desktopOutput `
            -File `
            -Recurse `
            -Force |
        Sort-Object FullName
    )

    $manifestRows = @()

    foreach ($file in $files) {
        if ($file.FullName -eq $manifestPath) {
            continue
        }

        $relative = $file.FullName.Substring(
            $desktopOutput.Length
        ).TrimStart('\')

        $manifestRows += [pscustomobject]@{
            RelativePath = $relative
            Length = $file.Length
            Sha256 = (
                Get-FileHash `
                    -LiteralPath $file.FullName `
                    -Algorithm SHA256
            ).Hash
        }
    }

    $manifestRows |
        Export-Csv `
            -LiteralPath $manifestPath `
            -NoTypeInformation `
            -Encoding UTF8

    New-Item `
        -ItemType Directory `
        -Path (
            Split-Path -Parent $OutputDirectory
        ) `
        -Force |
        Out-Null

    Copy-Item `
        -LiteralPath $desktopOutput `
        -Destination $OutputDirectory `
        -Recurse `
        -Force

    Compress-Archive `
        -Path (
            Join-Path $OutputDirectory "*"
        ) `
        -DestinationPath $zipPath `
        -CompressionLevel Optimal `
        -Force

    $packageHash = (
        Get-FileHash `
            -LiteralPath $zipPath `
            -Algorithm SHA256
    ).Hash

    Write-Host "===== DESKTOP PACKAGE COMPLETE ====="
    Write-Host "DESKTOP_EXE=$(
        Join-Path `
            $OutputDirectory `
            'AM.TribonAutomationProbe.Desktop.exe'
    )"
    Write-Host "CONSOLE_EXE=$(
        Join-Path `
            $OutputDirectory `
            'console\AM.TribonAutomationProbe.Console.exe'
    )"
    Write-Host "PACKAGE_ZIP=$zipPath"
    Write-Host "PACKAGE_SHA256=$packageHash"
    Write-Host "FILE_COUNT=$($manifestRows.Count)"
    Write-Host "INSTALLER_CREATED=False"
    Write-Host "AUTO_LAYOUT_INCLUDED=False"
    Write-Host "MULTI_DRAWING_REGRESSION_INCLUDED=False"
    Write-Host "ROUND4_4D3_DESKTOP_PUBLISH=PASS"
}
finally {
    Remove-Item `
        -LiteralPath $stagingRoot `
        -Recurse `
        -Force `
        -ErrorAction SilentlyContinue
}
