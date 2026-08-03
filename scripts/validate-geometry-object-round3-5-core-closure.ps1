$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $root 'tests\AM.TribonAutomationProbe.Tests\Fixtures\GeometryObjectPoc'
$d = Get-Content (Join-Path $fixture 'geometry-detection.formal.json') -Raw | ConvertFrom-Json
$b = Get-Content (Join-Path $fixture 'geometry-labels-before.formal.json') -Raw | ConvertFrom-Json
$a = Get-Content (Join-Path $fixture 'geometry-labels-after.formal.json') -Raw | ConvertFrom-Json
if ($d.objects.Count -ne 12 -or $d.diagnostics.assignedUniqueContourCount -ne 113 -or $d.diagnostics.unassignedContourCount -ne 23 -or $d.diagnostics.capturedContourCount -ne 136 -or $b.labels.Count -ne 12 -or $a.labels.Count -ne 12) { throw 'Formal fixture contract mismatch' }
$testProject = Join-Path $root 'tests\AM.TribonAutomationProbe.Tests\AM.TribonAutomationProbe.Tests.csproj'
$filters = @('GeometryCorePocClosureTests','GeometryLabelMatcherTests','GeometryLabelAuditTests','GeometryLabelPlannerTests','GeometryContractValidatorTests')
foreach ($filter in $filters) { dotnet test $testProject --no-restore --filter "FullyQualifiedName~$filter" --logger 'console;verbosity=minimal'; if ($LASTEXITCODE -ne 0) { throw "$filter failed" }; "$filter=PASS" }
'FORMAL_FIXTURE=PASS'; 'CORE_POC_CLOSURE=PASS'; 'MATCHER_TESTS=PASS'; 'AUDIT_TESTS=PASS'; 'PLANNER_TESTS=PASS'; 'CONTRACT_VALIDATOR_TESTS=PASS'; 'STATUS=PASS'
