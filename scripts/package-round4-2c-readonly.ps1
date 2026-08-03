param(
    [string]$Root = "D:\CodeNetSpace\AM.TribonAutomationProbe",
    [string]$RuntimeIdentifier = "win-x64"
)
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$Root = (Resolve-Path -LiteralPath $Root).Path
$project = Join-Path $Root "src\AM.TribonAutomationProbe.Console\AM.TribonAutomationProbe.Console.csproj"
$package = Join-Path $Root ("artifacts\test-machine\" + $RuntimeIdentifier)
$evidence = Join-Path $Root "artifacts\evidence"
New-Item -ItemType Directory -Force $evidence | Out-Null
if (Test-Path $package) { Remove-Item $package -Recurse -Force }
& dotnet publish $project -c Release -r $RuntimeIdentifier --self-contained true -o $package
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
$required = @("AM.TribonAutomationProbe.Console.exe", "AM.TribonAutomationProbe.Console.dll", "AM.TribonAutomationProbe.Adapter.OpenAI.dll", "AM.TribonAutomationProbe.Adapter.FileBridge.dll", "AM.TribonAutomationProbe.Core.dll")
foreach ($name in $required) { if (-not (Test-Path (Join-Path $package $name))) { throw "Required package file missing: $name" } }
$records = @()
foreach ($file in Get-ChildItem $package -Recurse -File) {
    $relative = $file.FullName.Substring($package.Length + 1).Replace('\','/')
    $records += [pscustomobject]@{ path=$relative; length=[int64]$file.Length; sha256=(Get-FileHash $file.FullName -Algorithm SHA256).Hash }
}
$records = @($records | Sort-Object path)
if (@($records | Group-Object path | Where-Object Count -gt 1).Count -gt 0) { throw "Duplicate manifest path" }
$lines = @("path`tlength`tsha256") + @($records | ForEach-Object { "$($_.path)`t$($_.length)`t$($_.sha256)" })
$manifest = Join-Path $evidence "round4-2c-test-console-package-manifest.tsv"
[IO.File]::WriteAllLines($manifest, $lines, (New-Object Text.UTF8Encoding($true)))
Copy-Item $manifest (Join-Path $evidence "round4-2c-test-console-package-manifest.txt") -Force
$manifestHash = (Get-FileHash $manifest -Algorithm SHA256).Hash
@("FORMAT=ROUND4_2C_CONSOLE_PACKAGE", "RID=$RuntimeIdentifier", "SELF_CONTAINED=True", "PACKAGE_ROOT=$package", "FILE_COUNT=$($records.Count)", "MANIFEST_SHA256=$manifestHash", "STATUS=SUCCESS") | Set-Content (Join-Path $evidence "round4-2c-test-console-package-result.txt")


# ROUND4_2C_DEPLOYMENT_BUNDLE_V2

$deploymentBase = Join-Path $Root "artifacts\deployment"
$deploymentRoot = Join-Path $deploymentBase "round4-2c"
$deploymentPackage = Join-Path $deploymentRoot "package"
$deploymentEvidence = Join-Path $deploymentRoot "evidence"
$deploymentTools = Join-Path $deploymentRoot "tools"
$zipPath = Join-Path $deploymentBase "round4-2c-test-machine.zip"

$installerSource = Join-Path $Root "tools\validation\install-round4-2c-test-console.ps1"
$interpretSmokeSource = Join-Path $Root "tools\validation\smoke-round4-2c-test-machine-interpret.ps1"
$detectSmokeSource = Join-Path $Root "tools\validation\smoke-round4-2c-llm-filebridge-tribon-detect.ps1"
$compoundSmokeSource = Join-Path $Root "tools\validation\smoke-round4-2c-llm-filebridge-tribon-detect-highlight-flanges.ps1"

$toolSources = @(
    $installerSource,
    $interpretSmokeSource,
    $detectSmokeSource,
    $compoundSmokeSource
)

foreach ($toolSource in $toolSources) {
    if (-not (Test-Path -LiteralPath $toolSource -PathType Leaf)) {
        throw "Required deployment tool missing: $toolSource"
    }

    $bytes = [IO.File]::ReadAllBytes($toolSource)

    if (
        $bytes.Length -lt 3 -or
        $bytes[0] -ne 239 -or
        $bytes[1] -ne 187 -or
        $bytes[2] -ne 191
    ) {
        throw "Deployment tool is missing UTF-8 BOM: $toolSource"
    }

    $parseTokens = $null
    $parseErrors = $null

    [void][System.Management.Automation.Language.Parser]::ParseFile(
        $toolSource,
        [ref]$parseTokens,
        [ref]$parseErrors
    )

    if (@($parseErrors).Count -ne 0) {
        throw "PowerShell syntax error: $toolSource"
    }
}

$installerText = [IO.File]::ReadAllText(
    $installerSource,
    [Text.Encoding]::UTF8
)

if ($installerText.Contains('`r`n')) {
    throw "Installer contains literal CRLF text."
}

if (
    -not $installerText.Contains(
        'Join-Path $bundleRoot "package"'
    )
) {
    throw "Installer does not support bundle package resolution."
}

if (
    -not $installerText.Contains(
        'Join-Path $bundleRoot "evidence\round4-2c-test-console-package-manifest.tsv"'
    )
) {
    throw "Installer does not support bundle manifest resolution."
}

foreach ($smokeSource in @(
    $detectSmokeSource,
    $compoundSmokeSource
)) {
    $smokeText = [IO.File]::ReadAllText(
        $smokeSource,
        [Text.Encoding]::UTF8
    )

    if (-not $smokeText.Contains('"--adapter=file-bridge"')) {
        throw "FileBridge adapter argument missing: $smokeSource"
    }

    if (-not $smokeText.Contains('"--bridge-root=$BridgeRoot"')) {
        throw "Bridge root argument missing: $smokeSource"
    }

    if (
        -not $smokeText.Contains(
            '"--timeout-ms=$($TimeoutSeconds*1000)"'
        )
    ) {
        throw "Timeout argument missing: $smokeSource"
    }
}

if (Test-Path -LiteralPath $deploymentRoot) {
    Remove-Item `
        -LiteralPath $deploymentRoot `
        -Recurse `
        -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item `
        -LiteralPath $zipPath `
        -Force
}

New-Item `
    -ItemType Directory `
    -Force `
    -Path `
        $deploymentPackage,
        $deploymentEvidence,
        $deploymentTools |
    Out-Null

foreach ($item in Get-ChildItem -LiteralPath $package) {
    Copy-Item `
        -LiteralPath $item.FullName `
        -Destination $deploymentPackage `
        -Recurse `
        -Force
}

$packageResult = Join-Path `
    $evidence `
    "round4-2c-test-console-package-result.txt"

foreach ($evidenceFile in @(
    $manifest,
    $packageResult
)) {
    if (-not (Test-Path -LiteralPath $evidenceFile -PathType Leaf)) {
        throw "Required evidence file missing: $evidenceFile"
    }

    Copy-Item `
        -LiteralPath $evidenceFile `
        -Destination $deploymentEvidence `
        -Force
}

foreach ($toolSource in $toolSources) {
    Copy-Item `
        -LiteralPath $toolSource `
        -Destination $deploymentTools `
        -Force
}

function Read-PackageManifest {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $header = Get-Content `
        -LiteralPath $Path `
        -TotalCount 1

    if ($header -ne "path`tlength`tsha256") {
        throw "Invalid package manifest header."
    }

    return @(
        Import-Csv `
            -LiteralPath $Path `
            -Delimiter "`t"
    )
}

function Test-PackageManifest {
    param(
        [Parameter(Mandatory)]
        [string]$PackageRoot,

        [Parameter(Mandatory)]
        [array]$Rows
    )

    $resolvedRoot = (
        Resolve-Path -LiteralPath $PackageRoot
    ).Path

    $actualPaths = @(
        Get-ChildItem `
            -LiteralPath $resolvedRoot `
            -Recurse `
            -File |
        ForEach-Object {
            $_.FullName.Substring(
                $resolvedRoot.Length + 1
            ).Replace("\", "/")
        } |
        Sort-Object
    )

    $listedPaths = @(
        $Rows |
        ForEach-Object {
            $_.path
        } |
        Sort-Object
    )

    $pathDifference = @(
        Compare-Object `
            -ReferenceObject $listedPaths `
            -DifferenceObject $actualPaths
    )

    if ($pathDifference.Count -ne 0) {
        $pathDifference | Format-Table | Out-String |
            Write-Host

        throw "Package files do not match manifest."
    }

    foreach ($row in $Rows) {
        if ($row.path -match '(^/|^[A-Za-z]:|\.\.)') {
            throw "Unsafe manifest path: $($row.path)"
        }

        $filePath = Join-Path `
            $resolvedRoot `
            ($row.path.Replace("/", "\"))

        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
            throw "Manifest file missing: $($row.path)"
        }

        $actualLength = [int64](
            Get-Item -LiteralPath $filePath
        ).Length

        if ($actualLength -ne [int64]$row.length) {
            throw "Length mismatch: $($row.path)"
        }

        $actualHash = (
            Get-FileHash `
                -LiteralPath $filePath `
                -Algorithm SHA256
        ).Hash

        if ($actualHash -ne $row.sha256) {
            throw "SHA-256 mismatch: $($row.path)"
        }
    }
}

function Get-FileHashMap {
    param(
        [Parameter(Mandatory)]
        [string]$BasePath
    )

    $resolvedBase = (
        Resolve-Path -LiteralPath $BasePath
    ).Path

    $map = @{}

    foreach (
        $file in Get-ChildItem `
            -LiteralPath $resolvedBase `
            -Recurse `
            -File
    ) {
        $relative = $file.FullName.Substring(
            $resolvedBase.Length + 1
        ).Replace("\", "/")

        $map[$relative] = (
            Get-FileHash `
                -LiteralPath $file.FullName `
                -Algorithm SHA256
        ).Hash
    }

    return $map
}

$deploymentManifest = Join-Path `
    $deploymentEvidence `
    "round4-2c-test-console-package-manifest.tsv"

$manifestRows = Read-PackageManifest `
    -Path $deploymentManifest

Test-PackageManifest `
    -PackageRoot $deploymentPackage `
    -Rows $manifestRows

Compress-Archive `
    -Path (Join-Path $deploymentRoot "*") `
    -DestinationPath $zipPath `
    -CompressionLevel Optimal `
    -Force

if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
    throw "Deployment ZIP was not created."
}

$verificationRoot = Join-Path `
    ([IO.Path]::GetTempPath()) `
    ("round4-2c-zip-verification-" + [Guid]::NewGuid().ToString("N"))

New-Item `
    -ItemType Directory `
    -Force `
    -Path $verificationRoot |
    Out-Null

try {
    Expand-Archive `
        -LiteralPath $zipPath `
        -DestinationPath $verificationRoot `
        -Force

    foreach ($requiredDirectory in @(
        "package",
        "evidence",
        "tools"
    )) {
        $requiredPath = Join-Path `
            $verificationRoot `
            $requiredDirectory

        if (
            -not (
                Test-Path `
                    -LiteralPath $requiredPath `
                    -PathType Container
            )
        ) {
            throw "ZIP directory missing: $requiredDirectory"
        }
    }

    $extractedManifest = Join-Path `
        $verificationRoot `
        "evidence\round4-2c-test-console-package-manifest.tsv"

    $extractedRows = Read-PackageManifest `
        -Path $extractedManifest

    Test-PackageManifest `
        -PackageRoot (
            Join-Path $verificationRoot "package"
        ) `
        -Rows $extractedRows

    $sourceMap = Get-FileHashMap `
        -BasePath $deploymentRoot

    $zipMap = Get-FileHashMap `
        -BasePath $verificationRoot

    $entryDifference = @(
        Compare-Object `
            -ReferenceObject @(
                $sourceMap.Keys |
                Sort-Object
            ) `
            -DifferenceObject @(
                $zipMap.Keys |
                Sort-Object
            )
    )

    if ($entryDifference.Count -ne 0) {
        $entryDifference | Format-Table | Out-String |
            Write-Host

        throw "ZIP entries do not match deployment directory."
    }

    foreach ($relativePath in $sourceMap.Keys) {
        if ($sourceMap[$relativePath] -ne $zipMap[$relativePath]) {
            throw "ZIP file hash mismatch: $relativePath"
        }
    }

    $extractedExe = Join-Path `
        $verificationRoot `
        "package\AM.TribonAutomationProbe.Console.exe"

    & $extractedExe --help | Out-Null

    $helpExitCode = $LASTEXITCODE

    if ($helpExitCode -ne 0) {
        throw "Extracted Console --help failed."
    }
}
finally {
    if (Test-Path -LiteralPath $verificationRoot) {
        Remove-Item `
            -LiteralPath $verificationRoot `
            -Recurse `
            -Force
    }
}

$zipHash = (
    Get-FileHash `
        -LiteralPath $zipPath `
        -Algorithm SHA256
).Hash

$deploymentFileCount = @(
    Get-ChildItem `
        -LiteralPath $deploymentRoot `
        -Recurse `
        -File
).Count

$zipResult = Join-Path `
    $evidence `
    "round4-2c-test-machine-zip-result.txt"

@(
    "FORMAT=ROUND4_2C_TEST_MACHINE_ZIP",
    "RID=$RuntimeIdentifier",
    "DEPLOYMENT_ROOT=$deploymentRoot",
    "ZIP_PATH=$zipPath",
    "ZIP_SHA256=$zipHash",
    "DEPLOYMENT_FILE_COUNT=$deploymentFileCount",
    "CONSOLE_MANIFEST_FILE_COUNT=$($manifestRows.Count)",
    "MANIFEST_VERIFICATION=PASS",
    "ZIP_ENTRY_VERIFICATION=PASS",
    "ZIP_HASH_VERIFICATION=PASS",
    "EXTRACTED_HELP_EXIT_CODE=0",
    "APIKeyRecorded=False",
    "FileBridgeRequestPerformed=False",
    "TribonConnected=False",
    "DrawingWritePerformed=False",
    "SavePerformed=False",
    "STATUS=SUCCESS"
) | Set-Content `
    -LiteralPath $zipResult `
    -Encoding UTF8

Write-Host ""
Write-Host "ROUND4_2C_PACKAGE=PASS"
Write-Host "PACKAGE_ROOT=$package"
Write-Host "DEPLOYMENT_ROOT=$deploymentRoot"
Write-Host "ZIP_PATH=$zipPath"
Write-Host "ZIP_SHA256=$zipHash"
Write-Host "CONSOLE_FILE_COUNT=$($manifestRows.Count)"
Write-Host "DEPLOYMENT_FILE_COUNT=$deploymentFileCount"