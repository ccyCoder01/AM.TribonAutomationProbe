# AM.TribonAutomationProbe Round 4.1A — Implementation-Only Landing Task

## 1. Mandatory outcome

Repository:

```text
D:\CodeNetSpace\AM.TribonAutomationProbe
```

This task is **repository implementation**, not real-machine certification.

You must modify the repository. Do not return `Blocked` merely because the coding
environment cannot launch Tribon.

The exact verified Vitesse source is present under:

```text
vendor\tribon-poc
```

Treat it as authoritative implementation input.

The maximum status allowed before test-machine execution is:

```text
Round 4.1A Implementation Complete
Real Tribon Integration Validation Pending
```

## 2. Why the previous attempts were invalid

Previous attempts stopped because they assumed all of the following had to be
available inside the coding environment:

```text
real Tribon process
real drawing context
verified highlight API
verified label API
existing dispatcher
```

Those assumptions are now resolved:

- verified API usage is supplied in `vendor\tribon-poc`;
- the existing FileBridge worker is supplied in
  `vendor\tribon-poc\bridge-worker\AMProbe_Start.py`;
- real-machine numerical results are supplied as evidence;
- real execution is a later acceptance step, not a prerequisite to code.

## 3. Required source landing

Create:

```text
vitesse\AddIns\AMGeometryObjectAutomation\Start.py
vitesse\AddIns\AMGeometryObjectAutomation\__init__.py
vitesse\AddIns\AMGeometryObjectAutomation\runtime\
```

Copy/refactor the verified POC algorithms into `runtime`. The deployed AddIn must
not depend on the audit ZIP or on files outside the repository package.

Preserve Python 2.3 compatibility:

```text
no f-strings
no with statement
no combined try/except/finally syntax
no Python 2.5+ syntax
```

## 4. Required actions

The dispatcher must process these FileBridge actions:

```text
geometry.detect
geometry.highlight-lifting
geometry.highlight-flanges
geometry.highlight-clear
geometry.label-preflight
geometry.label-apply-missing
```

Backward-compatible aliases may be supported, but the names above are canonical.

Every action must run in one Vitesse invocation:

```text
capture current drawing
→ extract current objects
→ build current runtime-handle map
→ execute action
→ verify action
→ write bridge result
```

Never load runtime handles from a previous command and use them directly.

## 5. Verified API contract

Use the supplied source implementations. The following calls are not speculative:

```python
region = KcsCaptureRegion2D.CaptureRegion2D()
region.SetBoundaryInfinite()
kcs_draft.contour_capture(region)
kcs_draft.element_highlight(handle)
kcs_draft.highlight_off(0)
kcs_draft.text_capture(region)
kcs_draft.text_properties_get(handle, KcsText.Text())
kcs_draft.text_new(KcsText.Text())
```

## 6. C# implementation

Add models and adapter methods for:

```text
GeometryDetectionRequest/Result
GeometryHighlightRequest/Result
GeometryHighlightClearRequest/Result
GeometryLabelPreflightRequest/Result
GeometryLabelApplyMissingRequest/Result
```

Add methods to `ITribonAdapter` or a dedicated geometry interface.

Implement FileBridge serialization/deserialization for all six actions.

Add Console commands:

```text
detect-geometry
highlight-lifting
highlight-flanges
clear-highlight
preflight-object-labels
apply-missing-object-labels
```

`apply-missing-object-labels` must reject locally unless:

```text
--allow-write=true
```

No other command may set `drawingWritePerformed=true`.

## 7. Label postcheck correction

Split postcheck into:

### Newly created labels

Strictly validate:

```text
text
unique match
planned X/Y within tolerance
planned height within tolerance
planned colour
source-object proximity
```

### Pre-existing labels

Validate only:

```text
text
unique match
source-object proximity
property-read success
```

Differences in X/Y/height/colour for pre-existing labels are diagnostics:

```text
POST_EXISTING_PROPERTY_DRIFT_COUNT
```

They must not turn an otherwise successful missing-label apply into
`FAILED_POSTCHECK`.

Required result fields:

```text
PRE_ALREADY_PRESENT_COUNT
PRE_MISSING_COUNT
PRE_DUPLICATE_TEXT_COUNT
PRE_INSPECTION_ERROR_COUNT
CREATED_COUNT
CREATE_FAILED_COUNT
POST_VALID_LABEL_COUNT
POST_MISSING_COUNT
POST_DUPLICATE_COUNT
POST_CREATED_VALID_COUNT
POST_CREATED_PROPERTY_ERROR_COUNT
POST_EXISTING_MATCH_ERROR_COUNT
POST_EXISTING_PROPERTY_DRIFT_COUNT
POST_INSPECTION_ERROR_COUNT
DRAWING_WRITE_PERFORMED
DRAWING_WRITE_COUNT
MANUAL_RECOVERY_REQUIRED
STATUS
```

## 8. Test requirements

Implement unit/Mock tests without Tribon.

At minimum:

1. detect returns 12 objects;
2. lifting selection returns 5 objects and 42 handles;
3. flange selection returns 7 objects and 71 handles;
4. clear highlight is zero-write;
5. label preflight returns 12 existing;
6. 10 existing + 2 missing creates only 2;
7. 10 existing labels with property drift plus 2 valid newly created labels returns `SUCCESS`;
8. new-label position error returns `FAILED_POSTCHECK`;
9. duplicate preflight blocks apply;
10. no `allowWrite` rejects locally;
11. runtime handles are marked current-invocation/nonpersistent;
12. existing Round 3.5 tests remain enabled.

## 9. Build and delivery

Run:

```powershell
dotnet build .\AM.TribonAutomationProbe.sln
dotnet test .\AM.TribonAutomationProbe.sln
```

Create:

```text
artifacts\evidence\round4-1a-implementation-result.txt
artifacts\evidence\round4-1a-source-inventory.txt
artifacts\evidence\round4-1a-package-manifest.tsv
artifacts\evidence\round4-1a-implementation.zip
```

The report must state:

```text
Build errors/warnings
Test count/pass/fail
Files added/modified
Six canonical actions
Six Console commands
Postcheck decision matrix
Known limitation: real Tribon deployment still pending
ZIP length and SHA-256
Manifest verification
```

## 10. Completion rule

Do not use either of these as a reason to avoid implementation:

```text
Tribon is unavailable in this coding environment
Real drawing cannot be opened here
```

When repository build/tests pass, report exactly:

```text
Round 4.1A Implementation Complete
Real Tribon Integration Validation Pending
```

Only the later test-machine run may promote it to `Verified`.
