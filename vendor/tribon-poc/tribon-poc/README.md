# Round 4.1A Authoritative Tribon POC Inputs

This directory is an implementation input, not an optional reference.

The source files were copied from the verified test-machine audit packages:

- `round4-0a-existing-poc-source-audit.zip`
- `round4-0b-existing-label-poc-source-audit.zip`

Verified Tribon/Vitesse APIs used by these sources:

```text
KcsCaptureRegion2D.CaptureRegion2D
SetBoundaryInfinite
kcs_draft.contour_capture
kcs_draft.contour_properties_get
kcs_draft.element_extent_get
kcs_draft.element_highlight
kcs_draft.highlight_off
kcs_draft.text_capture
kcs_draft.text_properties_get
kcs_draft.text_new
kcs_ui.app_window_refresh
```

Real test-machine results already established outside the repository:

```text
Captured contours:             136
Detected objects:               12
Assigned unique contours:      113
Unassigned contours:            23
Lifting highlight:           42/42
Flange highlight:            71/71
Labels after apply:           12/12
Repeated apply drawing writes:    0
Persistence after save/reopen: PASS
```

Important runtime constraint:

```text
Runtime handles are valid only for the current Vitesse invocation.
Capture, recognition, operation, and verification must run atomically.
```

Do not claim these files are unavailable. Do not block repository implementation
because the coding environment itself cannot start Tribon. Implement against these
authoritative sources and mark real integration validation as pending until the
generated build is deployed to the test machine.
