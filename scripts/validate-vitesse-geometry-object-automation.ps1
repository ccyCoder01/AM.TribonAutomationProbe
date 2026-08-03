$ErrorActionPreference = "Stop"
$path = Join-Path (Split-Path -Parent $PSScriptRoot) "vitesse\AddIns\AMGeometryObjectAutomation\Start.py"
$text = Get-Content -Raw $path
foreach ($token in @('drawing_extent_failed', 'allow_write', 'element_extent_get', 'status', 'succeeded')) { if (-not $text.Contains($token)) { throw "Missing token: $token" } }
foreach ($token in @('f-string', 'with open', 'dataclass', 'pathlib', 'element_child_first_get', 'element_sibling_next_get', 'text_new', 'note_new', 'posno_new', 'dwg_save', 'dwg_save_as', 'dwg_close', 'dwg_open', 'dwg_new', 'dwg_repaint', 'SUCCES')) { if ($text.Contains($token)) { throw "Forbidden token: $token" } }
if ($text -match 'except Exception as ') { throw "Modern exception syntax found" }
Write-Host "Static geometry object automation safety checks passed."
