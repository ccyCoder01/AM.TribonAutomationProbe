# Test-machine context smoke test

1. On the development machine run `powershell -ExecutionPolicy Bypass -File D:\CodeNetSpace\AM.TribonAutomationProbe\scripts\publish-test-machine.ps1`.
2. Copy the complete output directory to the test machine, for example `C:\AM\TribonAutomationProbe`.
3. Confirm `C:\AM_TribonBridge` contains `inbox`, `processing`, `output`, `archive`, `failed`, and `logs`.
4. Open Tribon M3 Drafting and the test drawing.
5. In test-machine PowerShell run:

```powershell
C:\AM\TribonAutomationProbe\AM.TribonAutomationProbe.Console.exe context --adapter=file-bridge --bridge-root=C:\AM_TribonBridge --timeout-ms=120000
```

6. While the command waits, click `Tools → Vitesse AddIns → AM Probe` in Tribon. The worker is currently manual, not a background poller.
7. Expected output is similar to `Context: succeeded (UNTITLED)`.
8. Verify the request leaves inbox, is absent from processing, has `request.json` in archive and its `result.json` in output. No `annotation.export` or `annotation.move` request should exist and the drawing must be unchanged.

The database name may be empty and view may be null; these known warnings do not fail this smoke test. Only `context.get` is accepted in this phase. Do not use file-bridge `move-annotation` or `run-all` during acceptance.
