$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root "vitesse\AddIns\AMAnnotationSurvey\Start.py"
$text = Get-Content -Raw $source
foreach ($token in @('schemaVersion', 'snapshotId', 'drawingExtent', 'parentExtent', 'labelExtent', 'runtimeHandle', 'childTextHandles', 'diagnostics', 'kcs_draft.element_extent_get', 'SetRectangle', 'SetInside', 'SetNoCut', 'posno_capture', 'dim_capture', 'text_capture', 'element_parent_get', 'text_properties_get')) { if (-not $text.Contains($token)) { throw "Missing required token: $token" } }
foreach ($token in @('SetBoundaryInfinite', 'note_capture', 'element_transform', 'element_transformation_redefine', 'text_properties_set', 'symbol_properties_set', 'dwg_properties_set', 'element_visibility_set', 'element_child_first_get', 'element_sibling_next_get', 'view_identify', 'kcs_ui')) { if ($text.Contains($token)) { throw "Forbidden token found: $token" } }
if ($text -match '(?m)^\s*[^#\r\n]+\s+if\s+[^\r\n]+\s+else\s+[^\r\n]+$') { throw "Python conditional expression found" }
foreach ($token in @('f-string', 'with open', 'dataclass', 'pathlib', 'typing', 'subprocess.run', 'except Exception as', 'GetX()', 'GetY()')) { if ($text.Contains($token)) { throw "Python 2.3 forbidden token found: $token" } }
$tryBlocks = [regex]::Matches($text, '(?ms)(^\s*)try:\s*\r?\n(.*?)(?=^\s*try:|^\s*except |^\s*finally:|\z)')
foreach ($block in $tryBlocks) { if ($block.Groups[2].Value -match '(?m)^\s*except Exception, e:' -and $block.Groups[2].Value -match '(?m)^\s*finally:') { throw "Python 2.3 incompatible try combination" } }
Write-Host "Static annotation snapshot safety checks passed."
