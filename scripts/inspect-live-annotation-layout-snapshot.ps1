param([Parameter(Mandatory=$true)][string]$SnapshotPath)
$ErrorActionPreference = "Stop"
$json = Get-Content -Raw -LiteralPath $SnapshotPath | ConvertFrom-Json
if ($json.schemaVersion -ne "1.0") { throw "schemaVersion must be 1.0" }
if ($json.scope -ne "current_drafting_context") { throw "Invalid scope" }
if ($json.handleScope -ne "current_drafting_session_only") { throw "Invalid handleScope" }
$items = @($json.items); $movable = @($items | Where-Object role -eq "movable"); $obstacles = @($items | Where-Object role -eq "obstacle")
$handles = @($items | ForEach-Object runtimeHandle); if (($handles | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) { throw "Empty runtimeHandle" }
$duplicates = @($handles | Group-Object | Where-Object Count -gt 1).Count
$childHandles = @($items | ForEach-Object { @($_.childTextHandles) }); $childDuplicates = @($childHandles | Group-Object | Where-Object Count -gt 1).Count
$invalid = @($items | Where-Object { $null -eq $_.labelExtent -or $_.labelExtent.x1 -gt $_.labelExtent.x2 -or $_.labelExtent.y1 -gt $_.labelExtent.y2 }).Count
$d = $json.diagnostics
Write-Host "SchemaVersion=$($json.schemaVersion)"; Write-Host "SnapshotId=$($json.snapshotId)"; Write-Host "Scope=$($json.scope)"; Write-Host "HandleScope=$($json.handleScope)"; Write-Host "DrawingExtent=$($json.drawingExtent.x1),$($json.drawingExtent.y1),$($json.drawingExtent.x2),$($json.drawingExtent.y2)"; Write-Host "ItemCount=$($items.Count)"; Write-Host "MovableCount=$($movable.Count)"; Write-Host "PositionNumberCount=$(@($items | Where-Object type -eq 'position_number').Count)"; Write-Host "DimensionCount=$(@($items | Where-Object type -eq 'dimension').Count)"; Write-Host "ObstacleCount=$($obstacles.Count)"; Write-Host "CapturedTextCount=$($d.capturedTextCount)"; Write-Host "IndependentTextCount=$($d.independentTextCount)"; Write-Host "PositionNumberChildTextCount=$($d.positionNumberChildTextCount)"; Write-Host "DimensionChildTextCount=$($d.dimensionChildTextCount)"; Write-Host "UnresolvedTextOwnerCount=$($d.unresolvedTextOwnerCount)"; Write-Host "LabelExtentFallbackCount=$($d.labelExtentFallbackCount)"; Write-Host "InvalidExtentCount=$invalid"; Write-Host "DuplicateRuntimeHandleCount=$duplicates"; Write-Host "DuplicateChildTextHandleCount=$childDuplicates"
if ($duplicates -gt 0 -or $childDuplicates -gt 0 -or $invalid -gt 0) { Write-Host "Status=FAIL"; exit 1 }
Write-Host "Status=PASS"
