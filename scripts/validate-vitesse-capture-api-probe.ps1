$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root "vitesse\AddIns\AMCaptureApiProbe"
$text = Get-Content -Raw (Join-Path $source "Start.py")
foreach ($token in @('"0.3"', 'DRAWING_EXTENT', 'import KcsCaptureRegion2D', 'import KcsPoint2D', 'import KcsRectangle2D', 'CaptureRegion2D()', 'SetBoundaryInfinite()', 'SetRectangle', 'SetInside', 'SetNoCut', 'text_capture', 'dim_capture', 'posno_capture', 'note_capture', 'capture-api-probe-kind.txt', 'capture-api-probe-region.txt', 'selected = "TEXT"', 'selected = "DRAWING_EXTENT"', 'subpicture_current_get', 'element_is_view', 'element_extent_get()', 'Corner1.X', 'Corner1.Y', 'Corner2.X', 'Corner2.Y', 'IsEmpty()', 'DRAWING_EXTENT_CALL_START', 'DRAWING_EXTENT_CALL_DONE', '_run_active')) { if (-not $text.Contains($token)) { throw "Missing required token: $token" } }
if ($text -match '(?m)^\s*[^#\r\n]+\s+if\s+[^\r\n]+\s+else\s+[^\r\n]+$') { throw "Python conditional expression found" }
foreach ($token in @('f-string', 'with open', 'dataclass', 'pathlib', 'element_transform', 'element_delete', 'text_new', 'note_new', 'posno_new', 'subpicture_current_set', 'dwg_save', 'dwg_save_as', 'dwg_close', 'dwg_open', 'dwg_new', 'dwg_repaint', 'highlight_off', 'element_highlight', 'delete_by_area', 'kcs_ui', 'GetX()', 'GetY()')) { if ($text.Contains($token)) { throw "Forbidden token found: $token" } }
if ($text -match 'except Exception as [A-Za-z_]') { throw "Python 3 exception syntax found" }
$tryBlocks = [regex]::Matches($text, '(?ms)(^\s*)try:\s*\r?\n(.*?)(?=^\s*try:|^\s*except |^\s*finally:|\z)')
foreach ($block in $tryBlocks) { if ($block.Groups[2].Value -match '(?m)^\s*except Exception, e:' -and $block.Groups[2].Value -match '(?m)^\s*finally:') { throw "Python 2.3 incompatible try combination" } }
Write-Host "Static capture API probe safety checks passed."
