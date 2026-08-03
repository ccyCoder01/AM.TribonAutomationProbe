[CmdletBinding()]
param(
    [string]$RepositoryRoot = "D:\CodeNetSpace\AM.TribonAutomationProbe",

    [string]$OutputZip = "",

    [switch]$KeepStagingDirectory
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$BasePath,
        [Parameter(Mandatory = $true)][string]$FullPath
    )

    $base = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\') + '\'
    $full = [System.IO.Path]::GetFullPath($FullPath)

    $baseUri = New-Object System.Uri($base)
    $fullUri = New-Object System.Uri($full)

    return [System.Uri]::UnescapeDataString(
        $baseUri.MakeRelativeUri($fullUri).ToString()
    ).Replace('/', '\')
}

function Test-ExcludedRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    $normalized = $RelativePath.Replace('/', '\')
    $segments = $normalized.Split('\')

    $excludedDirectories = @(
        ".git",
        ".vs",
        "bin",
        "obj",
        "TestResults",
        "node_modules",
        "packages",
        ".idea"
    )

    foreach ($segment in $segments) {
        if ($excludedDirectories -contains $segment) {
            return $true
        }
    }

    $leaf = [System.IO.Path]::GetFileName($normalized)

    if ($leaf -match '\.(zip|7z|rar|user|suo|tmp|bak)$') {
        return $true
    }

    if ($leaf -match '^~') {
        return $true
    }

    return $false
}

function Copy-ApprovedTree {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$DestinationRoot
    )

    if (-not (Test-Path -LiteralPath $SourceRoot -PathType Container)) {
        return
    }

    Get-ChildItem -LiteralPath $SourceRoot -File -Recurse -Force |
        ForEach-Object {
            $relative = Get-RelativePath -BasePath $RepositoryRoot -FullPath $_.FullName

            if (Test-ExcludedRelativePath -RelativePath $relative) {
                return
            }

            $destination = Join-Path $DestinationRoot $relative
            $destinationDirectory = Split-Path -Parent $destination

            New-Item -Path $destinationDirectory -ItemType Directory -Force |
                Out-Null

            Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
        }
}

function Get-Sha256 {
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

$RepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)

if (-not (Test-Path -LiteralPath $RepositoryRoot -PathType Container)) {
    throw "Repository not found: $RepositoryRoot"
}

if ([string]::IsNullOrWhiteSpace($OutputZip)) {
    $OutputZip = Join-Path `
        $RepositoryRoot `
        "artifacts\evidence\round4-1a-implementation.zip"
}

$OutputZip = [System.IO.Path]::GetFullPath($OutputZip)
$outputDirectory = Split-Path -Parent $OutputZip

New-Item -Path $outputDirectory -ItemType Directory -Force | Out-Null

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$stagingRoot = Join-Path `
    ([System.IO.Path]::GetTempPath()) `
    "AM.TribonAutomationProbe-round4-1a-$timestamp-$PID"

$verificationRoot = "$stagingRoot-verify"

Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $verificationRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $OutputZip -Force -ErrorAction SilentlyContinue

New-Item -Path $stagingRoot -ItemType Directory -Force | Out-Null

try {
    # Source trees included in the implementation package.
    $approvedDirectories = @(
        "src",
        "tests",
        "vitesse",
        "scripts",
        "vendor",
        "docs"
    )

    foreach ($directory in $approvedDirectories) {
        Copy-ApprovedTree `
            -SourceRoot (Join-Path $RepositoryRoot $directory) `
            -DestinationRoot $stagingRoot
    }

    # Evidence is included, but existing ZIP files are deliberately excluded.
    Copy-ApprovedTree `
        -SourceRoot (Join-Path $RepositoryRoot "artifacts\evidence") `
        -DestinationRoot $stagingRoot

    # Approved repository-root files.
    $rootPatterns = @(
        "*.sln",
        "*.slnx",
        "*.md",
        "*.txt",
        "global.json",
        "NuGet.config",
        "Directory.Build.*",
        "Directory.Packages.*",
        ".gitignore",
        ".editorconfig"
    )

    $rootFiles = @()

    foreach ($pattern in $rootPatterns) {
        $rootFiles += Get-ChildItem `
            -LiteralPath $RepositoryRoot `
            -File `
            -Force `
            -Filter $pattern `
            -ErrorAction SilentlyContinue
    }

    $rootFiles |
        Sort-Object FullName -Unique |
        ForEach-Object {
            $relative = Get-RelativePath `
                -BasePath $RepositoryRoot `
                -FullPath $_.FullName

            if (-not (Test-ExcludedRelativePath -RelativePath $relative)) {
                Copy-Item `
                    -LiteralPath $_.FullName `
                    -Destination (Join-Path $stagingRoot $relative) `
                    -Force
            }
        }

    $manifestRelativePath = "artifacts\evidence\round4-1a-package-manifest.tsv"
    $manifestPath = Join-Path $stagingRoot $manifestRelativePath
    $manifestDirectory = Split-Path -Parent $manifestPath

    New-Item -Path $manifestDirectory -ItemType Directory -Force | Out-Null
    Remove-Item -LiteralPath $manifestPath -Force -ErrorAction SilentlyContinue

    # The manifest lists every ZIP payload file except itself.
    $payloadFiles = Get-ChildItem -LiteralPath $stagingRoot -File -Recurse -Force |
        Where-Object {
            (Get-RelativePath -BasePath $stagingRoot -FullPath $_.FullName) `
                -ne $manifestRelativePath
        } |
        Sort-Object FullName

    $tab = [char]9
    $manifestLines = New-Object System.Collections.Generic.List[string]
    $manifestLines.Add(
        "RelativePath${tab}Length${tab}SHA256"
    )

    foreach ($file in $payloadFiles) {
        $relative = Get-RelativePath `
            -BasePath $stagingRoot `
            -FullPath $file.FullName

        $manifestLines.Add(
            "$relative$tab$($file.Length)$tab$(Get-Sha256 -Path $file.FullName)"
        )
    }

    [System.IO.File]::WriteAllLines(
        $manifestPath,
        $manifestLines.ToArray(),
        (New-Object System.Text.UTF8Encoding($false))
    )

    Compress-Archive `
        -Path (Join-Path $stagingRoot "*") `
        -DestinationPath $OutputZip `
        -CompressionLevel Optimal `
        -Force

    # Verify the actual ZIP by extracting it and comparing every manifest row.
    Expand-Archive `
        -LiteralPath $OutputZip `
        -DestinationPath $verificationRoot `
        -Force

    $manifestRows = Import-Csv `
        -LiteralPath (Join-Path $verificationRoot $manifestRelativePath) `
        -Delimiter "`t"

    $manifestMap = @{}

    foreach ($row in $manifestRows) {
        $manifestMap[$row.RelativePath] = $row
    }

    $verificationFiles = Get-ChildItem `
        -LiteralPath $verificationRoot `
        -File `
        -Recurse `
        -Force |
        Where-Object {
            (Get-RelativePath `
                -BasePath $verificationRoot `
                -FullPath $_.FullName) -ne $manifestRelativePath
        }

    $errors = New-Object System.Collections.Generic.List[string]

    foreach ($file in $verificationFiles) {
        $relative = Get-RelativePath `
            -BasePath $verificationRoot `
            -FullPath $file.FullName

        if (-not $manifestMap.ContainsKey($relative)) {
            $errors.Add("Unexpected ZIP entry: $relative")
            continue
        }

        $expected = $manifestMap[$relative]

        if ([int64]$expected.Length -ne $file.Length) {
            $errors.Add(
                "Length mismatch: $relative expected=$($expected.Length) actual=$($file.Length)"
            )
        }

        $actualHash = Get-Sha256 -Path $file.FullName

        if ($expected.SHA256.ToUpperInvariant() -ne $actualHash) {
            $errors.Add(
                "SHA256 mismatch: $relative expected=$($expected.SHA256) actual=$actualHash"
            )
        }

        $manifestMap.Remove($relative)
    }

    foreach ($missing in $manifestMap.Keys) {
        $errors.Add("Missing ZIP entry: $missing")
    }

    if ($errors.Count -gt 0) {
        $errors | ForEach-Object { Write-Error $_ }
        throw "Package verification failed with $($errors.Count) error(s)."
    }

    $zipHash = Get-Sha256 -Path $OutputZip
    $zipLength = (Get-Item -LiteralPath $OutputZip).Length
    $entryCount = (
        Get-ChildItem -LiteralPath $verificationRoot -File -Recurse -Force
    ).Count

    Write-Host ""
    Write-Host "===== ROUND 4.1A PACKAGE RESULT ====="
    Write-Host "RepositoryRoot       : $RepositoryRoot"
    Write-Host "OutputZip            : $OutputZip"
    Write-Host "ZIPLength            : $zipLength"
    Write-Host "ZIPSHA256            : $zipHash"
    Write-Host "ZIPFileEntryCount    : $entryCount"
    Write-Host "ManifestPayloadCount : $($manifestRows.Count)"
    Write-Host "ManifestVerification : PASS"
    Write-Host "Excluded              : .git, .vs, bin, obj, TestResults, ZIPs"
    Write-Host "STATUS                : SUCCESS"
}
finally {
    Remove-Item `
        -LiteralPath $verificationRoot `
        -Recurse `
        -Force `
        -ErrorAction SilentlyContinue

    if (-not $KeepStagingDirectory) {
        Remove-Item `
            -LiteralPath $stagingRoot `
            -Recurse `
            -Force `
            -ErrorAction SilentlyContinue
    }
    else {
        Write-Host "StagingDirectory      : $stagingRoot"
    }
}
