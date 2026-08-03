# AMAnnotationSurvey

`AMAnnotationSurvey` is an independent, strictly read-only runtime survey AddIn. It is diagnostic research, not the formal `annotation.export` implementation. It does not modify `AMProbe` or the existing context worker.

The survey uses only these read APIs: `dwg_name_get`, `subpicture_current_get`, `element_child_first_get`, `element_sibling_next_get`, `element_parent_get`, `element_extent_get`, `subpicture_name_get`, the `element_is_*` classifiers, `text_properties_get`, and `KcsText.Text` getters. No write, create, delete, move, transform, save, open, close, repaint, or current-subpicture-set API is used.

## Package and deploy

Run `powershell -ExecutionPolicy Bypass -File D:\CodeNetSpace\AM.TribonAutomationProbe\scripts\package-vitesse-annotation-survey.ps1`, inspect the ZIP, and back up the test machine's existing Vitesse AddIns. Copy the extracted `AMAnnotationSurvey` directory to:

`C:\Tribon\M3\Vitesse\AddIns\AMAnnotationSurvey`

Do not copy files directly into the repository's test-machine bridge path or modify the existing AddIn.

## Three-run comparison

1. Fully close and reopen Drafting.
2. Open a drawing containing ordinary text, General Note, Position Number, and Dimension.
3. Run `Tools → Vitesse AddIns → AM Annotation Survey` once; retain the JSON.
4. Without changing or saving the drawing, run it a second time; retain the JSON.
5. Fully close Drafting, reopen the same drawing, and run it a third time.
6. Compare run one with run two for same-session stability, and run two with run three for restart stability. Compare `runtimeHandle`, `ancestorPath`, text, coordinates, extent text, and Note/Position Number/Dimension child hierarchy.
7. Do not save the drawing and do not select any write operation.

Results are written below `C:\AM_TribonBridge\diagnostics` as `annotation-survey-YYYYMMDDTHHMMSS-PID.json`; the log is `C:\AM_TribonBridge\logs\am_annotation_survey.log`. `runtimeHandle` has not been proven stable across sessions and is not a durable identity. `identityCandidate` is only a comparison aid and is not guaranteed unique.

The local source did not contain a verified Rectangle2D getter, so the survey records `extentText` and warns that structured coordinates were not resolved. It intentionally does not infer leader points or active-view semantics.

## Open questions

Runtime-handle cross-session stability, a durable identity strategy, the real Note/PosNo/Dimension property structure, leader points, and active-view resolution remain unresolved.
