$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $root 'tests\AM.TribonAutomationProbe.Tests\Fixtures\GeometryObjectPoc'
$legacy = Join-Path $fixture 'geometry-object-snapshot.legacy.json'
$plan = Join-Path $fixture 'geometry-object-label-relayout-plan-before.legacy.tsv'
$snapshot = Get-Content -Raw -LiteralPath $legacy | ConvertFrom-Json
$objects = foreach ($o in $snapshot.objects) {
  [ordered]@{ runtimeObjectId=$o.objectId; category=$o.category; confidence='high'; extent=[ordered]@{ x1=$o.extent.minX; y1=$o.extent.minY; x2=$o.extent.maxX; y2=$o.extent.maxY }; seedHandles=@($o.seedHandles); geometryHandles=@($o.geometryHandles); geometryCount=$o.geometryCount; features=[ordered]@{ geometryCount=$o.geometryCount } }
}
$detection = [ordered]@{ schemaVersion='1.0'; requestId='formal-fixture'; status='succeeded'; scope='current_drawing_contours'; drawingExtent=[ordered]@{ x1=10; y1=10; x2=410; y2=287 }; objects=@($objects); diagnostics=[ordered]@{ capturedContourCount=136; assignedUniqueContourCount=113; unassignedContourCount=23; conflictHandleCount=0; parseFailureCount=0 } }
$labels = @()
foreach ($line in (Get-Content -LiteralPath $plan | Select-Object -Skip 4 | Where-Object { $_ -and $_ -notmatch '^SUMMARY' })) {
  $p = $line -split "`t"; if ($p.Count -ge 12 -and $p[0] -match '^\d+$') { $e = $p[4] -split ','; $labels += [ordered]@{ runtimeHandle=$p[3]; text=$p[2]; extent=[ordered]@{ x1=[double]$e[0]; y1=[double]$e[1]; x2=[double]$e[2]; y2=[double]$e[3] } } }
}
$after = [ordered]@{ schemaVersion='1.0'; requestId='formal-labels-after'; status='succeeded'; labels=@($labels); diagnostics=[ordered]@{ duplicateTextCount=0; extentReadFailureCount=0; textPropertyFailureCount=0 } }
$beforeLabels = @($labels | ForEach-Object { if ($_.text -eq 'LB-02') { [ordered]@{ runtimeHandle=$_.runtimeHandle; text=$_.text; extent=[ordered]@{ x1=52; y1=203; x2=67.199996948; y2=206.199996948 } } } else { $_ } })
$before = [ordered]@{ schemaVersion='1.0'; requestId='formal-labels-before'; status='succeeded'; labels=$beforeLabels; diagnostics=[ordered]@{ duplicateTextCount=0; extentReadFailureCount=0; textPropertyFailureCount=0 } }
foreach ($pair in @(@('geometry-detection.formal.json',$detection),@('geometry-labels-before.formal.json',$before),@('geometry-labels-after.formal.json',$after))) { $text = ($pair[1] | ConvertTo-Json -Depth 20); [IO.File]::WriteAllText((Join-Path $fixture $pair[0]), $text, [Text.UTF8Encoding]::new($false)) }
