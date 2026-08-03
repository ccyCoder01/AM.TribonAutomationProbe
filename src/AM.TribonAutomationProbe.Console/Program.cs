using System.Globalization;
using System.Text;
using System.Text.Json;
using AM.TribonAutomationProbe.Adapter.FileBridge;
using AM.TribonAutomationProbe.Adapter.Mock;
using AM.TribonAutomationProbe.Adapter.OpenAI;
using AM.TribonAutomationProbe.Core;

var parsed = CliParser.Parse(args, Environment.GetEnvironmentVariable("AM_TRIBON_BRIDGE_ROOT"));
if (parsed.ShowHelp) { Console.WriteLine(UsageText.Value); return 0; }
if (parsed.Error is not null) { Console.Error.WriteLine(parsed.Error); Console.Error.WriteLine(UsageText.Value); return 2; }
var options = parsed.Options!;
if (options.Adapter == "file-bridge" && options.RequiresWrite && !options.AllowWrite)
{ Console.Error.WriteLine("Real Tribon write operation requires --allow-write=true"); return 2; }

ITribonAdapter adapter = options.Adapter == "mock"
    ? new MockTribonAdapter()
    : new FileBridgeTribonAdapter(new FileBridgeTransport(new FileBridgeOptions(options.BridgeRoot, options.PollIntervalMs, options.TimeoutMs)));
var runner = new ProbeRunner(adapter);
IGeometryAutomationAdapter geometry = options.Adapter == "file-bridge"
    ? new FileBridgeGeometryAutomationAdapter(new FileBridgeTransport(new FileBridgeOptions(options.BridgeRoot, options.PollIntervalMs, options.TimeoutMs)))
    : new GeometryAutomationAdapter(adapter);
try
{
    switch (options.Command)
    {
        case "assistant":
        {
            var text = RequireAssistantText(options);
            var languageModel = AssistantLanguageModelFactory.Create(options);
            var planner = new AssistantTaskPlanner();
            var formatter = new AssistantResultFormatter();
            var orchestrator = new AssistantTaskOrchestrator(
                languageModel,
                planner,
                geometry,
                formatter);

            var result = await orchestrator.RunAsync(
                new AssistantConversationContext(text),
                new AssistantExecutionAuthorization(
                    AllowWrite: options.AllowWrite,
                    WriteConfirmed: options.ConfirmWrite),
                cancellationToken: CancellationToken.None);

            Console.WriteLine(JsonSerializer.Serialize(result, JsonDefaults.Options));
            return result.State == AssistantTaskState.Failed ? 1 : 0;
        }
        case "assistant-interpret":
        {
            var text = RequireAssistantText(options);
            var languageModel = AssistantLanguageModelFactory.Create(options);
            var interpretation = await languageModel.InterpretAsync(
                new AssistantConversationContext(text),
                CancellationToken.None);
            var plan = new AssistantTaskPlanner().CreatePlan(
                new AssistantConversationContext(text),
                interpretation);

            Console.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = "1.0",
                        productName = AssistantTaskOrchestrator.ProductName,
                        interpretation,
                        plan,
                        executionPerformed = false,
                        drawingWritePerformed = false,
                        savePerformed = false
                    },
                    JsonDefaults.Options));
            return 0;
        }
        case "assistant-run":
        {
            var text = RequireAssistantText(options);
            var languageModel = AssistantLanguageModelFactory.Create(options);
            var assistantContext = new AssistantConversationContext(text);
            var interpretation = await languageModel.InterpretAsync(assistantContext, CancellationToken.None);
            var plan = new AssistantTaskPlanner().CreatePlan(assistantContext, interpretation);
            var executeRequested = string.Equals(options.Get("execute"), "true", StringComparison.OrdinalIgnoreCase);
            if (!executeRequested)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { interpretation, plan, executionProfile = "round4-2c-readonly", executionRequested = false, executionPerformed = false, drawingWritePerformed = false, savePerformed = false }, JsonDefaults.Options));
                return 0;
            }
            if (options.Get("execution-profile") != "round4-2c-readonly") throw new ProbeException("ASSISTANT_EXECUTION_PROFILE_REJECTED", "assistant-run requires --execution-profile=round4-2c-readonly", "safety");
            ValidateRound42CPlan(plan);
            if (options.Adapter != "file-bridge") throw new ProbeException("ASSISTANT_EXECUTION_PROFILE_REJECTED", "round4-2c-readonly requires --adapter=file-bridge", "safety");
            var orchestrator = new AssistantTaskOrchestrator(languageModel, new AssistantTaskPlanner(), geometry, new AssistantResultFormatter());
            var run = await orchestrator.RunAsync(assistantContext, new AssistantExecutionAuthorization(false, false), cancellationToken: CancellationToken.None);
            Console.WriteLine(JsonSerializer.Serialize(new { interpretation, plan, executionProfile = "round4-2c-readonly", executionRequested = true, executionPerformed = run.State == AssistantTaskState.Completed, drawingWritePerformed = run.TaskResults.Any(x => x.DrawingWritePerformed), savePerformed = run.TaskResults.Any(x => x.SavePerformed), run, status = run.State == AssistantTaskState.Completed ? "SUCCESS" : "FAILED" }, JsonDefaults.Options));
            return run.State == AssistantTaskState.Completed ? 0 : 1;
        }
        case "context": var context = await adapter.GetContextAsync(CancellationToken.None); Console.WriteLine($"Context: succeeded ({context.Context.Drawing?.Name ?? "UNTITLED"})"); break;
        case "export-annotations": var export = await adapter.ExportAnnotationsAsync(new AnnotationExportRequest { Types = (options.Get("types") ?? "label,dimension,general_text").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) }, CancellationToken.None); Console.WriteLine($"Export annotations: succeeded ({export.Annotations.Count})"); break;
        case "move-annotation": var all = await adapter.ExportAnnotationsAsync(new AnnotationExportRequest(), CancellationToken.None); var selected = all.Annotations.FirstOrDefault(a => options.Get("object-id") is null || a.ObjectRef.PersistentId == options.Get("object-id")) ?? throw new ProbeException(ProbeErrorCodes.ObjectNotFound, "No annotation selected", "context"); var x = double.Parse(options.Get("x") ?? "130", CultureInfo.InvariantCulture); var y = double.Parse(options.Get("y") ?? "95", CultureInfo.InvariantCulture); await adapter.MoveAnnotationAsync(new MoveAnnotationRequest { ObjectRef = selected.ObjectRef, ExpectedPosition = selected.Position, DesiredPosition = new(x, y) }, CancellationToken.None); Console.WriteLine("Move annotation: succeeded"); break;
        case "run-all": await runner.RunAllAsync(CancellationToken.None); break;
        case "plan-annotation-layout":
            var snapshotPath = options.Get("snapshot") ?? Path.Combine(options.BridgeRoot, "diagnostics", "annotation-layout-snapshot.json");
            var planPath = options.Get("output") ?? Path.Combine(options.BridgeRoot, "responses", "annotation-layout-plan.json");
            var layoutSnapshot = LayoutJson.ReadSnapshot(snapshotPath);
            var layoutPlan = AnnotationLayoutPlanner.Plan(layoutSnapshot);
            Directory.CreateDirectory(Path.GetDirectoryName(planPath)!); LayoutJson.WritePlan(planPath, layoutPlan);
            Console.WriteLine($"movable={layoutSnapshot.Items.Count(x => x.Role == "movable")}"); Console.WriteLine($"obstacle={layoutSnapshot.Items.Count(x => x.Role == "obstacle")}"); Console.WriteLine($"initial conflict count={layoutPlan.InitialConflictCount}"); Console.WriteLine($"planned move count={layoutPlan.Moves.Count}"); Console.WriteLine($"remaining conflict count={layoutPlan.RemainingConflictCount}"); Console.WriteLine($"plan status={layoutPlan.Status}"); break;
        case "detect-geometry-objects":
            var detected = await adapter.DetectGeometryObjectsAsync(new GeometryObjectDetectionRequest(RequestId: Guid.NewGuid().ToString("N")), CancellationToken.None); var detectOutput = options.Get("output") ?? Path.Combine(options.BridgeRoot, "diagnostics", "geometry-object-snapshot.json"); Directory.CreateDirectory(Path.GetDirectoryName(detectOutput)!); File.WriteAllText(detectOutput, JsonSerializer.Serialize(detected, JsonDefaults.Options)); Console.WriteLine($"Detection: {detected.Status} ({detected.Objects.Count} objects)"); break;
        case "detect-geometry": { var result = await geometry.DetectAsync(new GeometryDetectionRequest(OperationId: Guid.NewGuid().ToString("N")), CancellationToken.None); Console.WriteLine(JsonSerializer.Serialize(result, JsonDefaults.Options)); break; }
        case "highlight-lifting": { var result = await geometry.HighlightAsync(new GeometryHighlightRequest(TaskType: "geometry.highlight-lifting", OperationId: Guid.NewGuid().ToString("N"), Categories: new GeometryObjectCategory[] { GeometryObjectCategory.LIFTING_BEAM, GeometryObjectCategory.LIFTING_LUG }), CancellationToken.None); Console.WriteLine(JsonSerializer.Serialize(result, JsonDefaults.Options)); break; }
        case "highlight-flanges": { var result = await geometry.HighlightAsync(new GeometryHighlightRequest(TaskType: "geometry.highlight-flanges", OperationId: Guid.NewGuid().ToString("N"), Categories: new GeometryObjectCategory[] { GeometryObjectCategory.PIPE_FLANGE_FRONT, GeometryObjectCategory.PIPE_FLANGE_SIDE, GeometryObjectCategory.STRUCTURAL_FLANGE }), CancellationToken.None); Console.WriteLine(JsonSerializer.Serialize(result, JsonDefaults.Options)); break; }
        case "clear-highlight": Console.WriteLine(JsonSerializer.Serialize(await geometry.ClearHighlightAsync(new GeometryHighlightClearRequest(OperationId: Guid.NewGuid().ToString("N")), CancellationToken.None), JsonDefaults.Options)); break;
        case "preflight-object-labels": Console.WriteLine(JsonSerializer.Serialize(await geometry.PreflightLabelsAsync(new GeometryLabelPreflightRequest(OperationId: Guid.NewGuid().ToString("N")), CancellationToken.None), JsonDefaults.Options)); break;
        case "apply-missing-object-labels": if (!options.AllowWrite) { Console.Error.WriteLine("apply-missing-object-labels requires --allow-write=true"); return 2; } Console.WriteLine(JsonSerializer.Serialize(await geometry.ApplyMissingLabelsAsync(new GeometryLabelApplyMissingRequest(OperationId: Guid.NewGuid().ToString("N"), AllowWrite: true), CancellationToken.None), JsonDefaults.Options)); break;
        case "audit-geometry-object-labels":
            var auditSnapshot = JsonSerializer.Deserialize<GeometryObjectDetectionResponse>(File.ReadAllText(options.Get("snapshot") ?? throw new ProbeException(ProbeErrorCodes.InvalidMessage, "--snapshot is required", "validation")), JsonDefaults.Options) ?? throw new ProbeException(ProbeErrorCodes.InvalidMessage, "Invalid snapshot", "validation");
            var auditLabels = JsonSerializer.Deserialize<GeometryLabelInspectionResponse>(File.ReadAllText(options.Get("labels") ?? throw new ProbeException(ProbeErrorCodes.InvalidMessage, "--labels is required", "validation")), JsonDefaults.Options) ?? throw new ProbeException(ProbeErrorCodes.InvalidMessage, "Invalid labels", "validation");
            var auditIds = GeometryObjectDisplayIdAssigner.Assign(auditSnapshot.Objects); var audit = GeometryObjectLabelAuditService.Audit(GeometryObjectLabelMatcher.Match(auditSnapshot.Objects, auditIds, auditLabels.Labels), auditLabels.Labels); var auditPath = options.Get("output") ?? "geometry-object-label-audit.json"; File.WriteAllText(auditPath, JsonSerializer.Serialize(audit, JsonDefaults.Options)); Console.WriteLine($"Audit: {audit.Status}, needsRelayout={audit.NeedsRelayoutCount}"); break;
        case "plan-geometry-object-labels":
            var planSnapshot = JsonSerializer.Deserialize<GeometryObjectDetectionResponse>(File.ReadAllText(options.Get("snapshot") ?? throw new ProbeException(ProbeErrorCodes.InvalidMessage, "--snapshot is required", "validation")), JsonDefaults.Options) ?? throw new ProbeException(ProbeErrorCodes.InvalidMessage, "Invalid snapshot", "validation");
            var planLabels = JsonSerializer.Deserialize<GeometryLabelInspectionResponse>(File.ReadAllText(options.Get("labels") ?? throw new ProbeException(ProbeErrorCodes.InvalidMessage, "--labels is required", "validation")), JsonDefaults.Options) ?? throw new ProbeException(ProbeErrorCodes.InvalidMessage, "Invalid labels", "validation");
            var planIds = GeometryObjectDisplayIdAssigner.Assign(planSnapshot.Objects); var planAudit = GeometryObjectLabelAuditService.Audit(GeometryObjectLabelMatcher.Match(planSnapshot.Objects, planIds, planLabels.Labels), planLabels.Labels); var geometryPlan = GeometryObjectLabelLayoutPlanner.Plan(planAudit, planSnapshot.DrawingExtent); var geometryPlanPath = options.Get("output") ?? "geometry-object-label-layout-plan.json"; File.WriteAllText(geometryPlanPath, JsonSerializer.Serialize(geometryPlan, JsonDefaults.Options)); Console.WriteLine($"Plan: {geometryPlan.Status}, moves={geometryPlan.MoveCount}"); break;
        case "apply-geometry-object-label-layout":
            if (options.RequiresWrite && !options.AllowWrite) { Console.Error.WriteLine("Geometry label apply requires --allow-write=true"); return 2; }
            var planFile = options.Get("plan"); if (string.IsNullOrWhiteSpace(planFile)) { Console.Error.WriteLine("--plan is required"); return 2; }
            var formalPlan = JsonSerializer.Deserialize<GeometryObjectLabelPlan>(File.ReadAllText(planFile), JsonDefaults.Options) ?? throw new ProbeException(ProbeErrorCodes.InvalidMessage, "Invalid geometry label plan", "validation");
            if (formalPlan.Status is not ("planned" or "no_changes_required")) throw new ProbeException(ProbeErrorCodes.InvalidMessage, "Plan is not executable", "validation");
            if (formalPlan.Moves.Count == 0) { Console.WriteLine("Apply: succeeded, no_changes_required"); break; }
            var applyRequest = new GeometryObjectLabelApplyRequest("1.0", "geometry_labels.apply_moves", Guid.NewGuid().ToString("N"), options.AllowWrite, formalPlan.Moves);
            var applied = await adapter.ApplyGeometryLabelMovesAsync(applyRequest, CancellationToken.None); Console.WriteLine($"Apply: {applied.Status}, savePerformed={applied.SavePerformed}, receipts={applied.Receipts.Count}"); break;
    }
    return 0;
}
catch (ProbeException ex) { Console.Error.WriteLine($"{ex.Code}: {ex.Message}"); return 1; }

static void ValidateRound42CPlan(AssistantTaskPlan plan)
{
    if (plan is null || plan.Tasks.Count < 1 || plan.Tasks.Count > 4) throw new ProbeException("ASSISTANT_PLAN_NOT_READ_ONLY", "Plan task count is outside the readonly profile.", "safety");
    if (plan.AutoSave || plan.Tasks.Any(x => x.AutoSave || x.Risk != AssistantTaskRisk.ReadOnly || x.TaskType is not ("geometry.detect" or "geometry.highlight-flanges"))) throw new ProbeException("ASSISTANT_PLAN_NOT_READ_ONLY", "Plan contains a task outside the round4-2c readonly whitelist.", "safety");
    if (plan.Tasks.Select(x => x.TaskType).Distinct(StringComparer.Ordinal).Count() != plan.Tasks.Count) throw new ProbeException("ASSISTANT_PLAN_NOT_READ_ONLY", "Plan contains duplicate tasks.", "safety");
}

static string RequireAssistantText(CliOptions options)
{
    var text = options.Get("text");

    if (string.IsNullOrWhiteSpace(text))
    {
        throw new ProbeException(
            ProbeErrorCodes.InvalidMessage,
            "assistant requires --text=<natural-language-instruction>",
            "validation");
    }

    return text;
}

public sealed record CliOptions(
    string Command,
    string Adapter,
    string BridgeRoot,
    int TimeoutMs,
    int PollIntervalMs,
    bool AllowWrite,
    bool ConfirmWrite,
    string? AssistantBaseUrl,
    string? AssistantModel,
    IReadOnlyDictionary<string, string> Values)
{
    public bool RequiresWrite => Command is "move-annotation" or "run-all" or "apply-geometry-object-label-layout" or "apply-missing-object-labels";
    public string? Get(string name) => Values.TryGetValue(name, out var value) ? value : null;
}
public sealed record CliParseResult(CliOptions? Options, bool ShowHelp, string? Error);
public static class CliParser
{
    public static CliParseResult Parse(string[] args, string? environmentRoot = null)
    {
        if (args.Any(a => a is "--help" or "-h")) return new(null, true, null);
        var command = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        if (command is null) return new(null, false, "A command must be specified explicitly.");
        if (command is not ("assistant" or "assistant-interpret" or "assistant-run" or "context" or "export-annotations" or "move-annotation" or "run-all" or "plan-annotation-layout" or "detect-geometry-objects" or "detect-geometry" or "highlight-lifting" or "highlight-flanges" or "clear-highlight" or "preflight-object-labels" or "apply-missing-object-labels" or "plan-geometry-object-labels" or "apply-geometry-object-label-layout" or "audit-geometry-object-labels")) return new(null, false, $"Invalid command: {command}");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var arg in args.Where(a => a.StartsWith("--"))) { var parts = arg[2..].Split('=', 2); if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[1])) return new(null, false, $"Invalid option: {arg}"); values[parts[0]] = parts[1]; }
        var adapter = values.TryGetValue("adapter", out var a) ? a : "mock";
        if (adapter is not ("mock" or "file-bridge")) return new(null, false, $"Invalid adapter: {adapter}");
        if (!Positive(values, "timeout-ms", 30000, out var timeout, out var error) || !Positive(values, "poll-interval-ms", 200, out var poll, out error)) return new(null, false, error);
        var root = values.TryGetValue("bridge-root", out var cliRoot) ? cliRoot : (!string.IsNullOrWhiteSpace(environmentRoot) ? environmentRoot : "./tribon-bridge");
        try { root = Path.GetFullPath(root); } catch { return new(null, false, "Invalid bridge-root path."); }
        var allow = values.TryGetValue("allow-write", out var write) && write.Equals("true", StringComparison.OrdinalIgnoreCase);
        var confirm = values.TryGetValue("confirm-write", out var confirmed) && confirmed.Equals("true", StringComparison.OrdinalIgnoreCase);
        if (values.ContainsKey("api-key") || values.Keys.Any(x => x.StartsWith("assistant-", StringComparison.OrdinalIgnoreCase) && x is not "assistant-base-url" and not "assistant-model")) return new(null, false, "Only --base-url and --model are supported for assistant model configuration.");
        var baseUrl = values.TryGetValue("base-url", out var baseUrlValue) ? baseUrlValue : null;
        var model = values.TryGetValue("assistant-model", out var modelValue) ? modelValue : null;
        if (values.TryGetValue("model", out var modelOverride)) model = modelOverride;
        return new(new(command, adapter, root, timeout, poll, allow, confirm, baseUrl, model, values), false, null);
    }
    static bool Positive(Dictionary<string,string> values, string key, int fallback, out int value, out string? error) { error = null; value = fallback; if (!values.TryGetValue(key, out var text)) return true; if (!int.TryParse(text, out value) || value <= 0) { error = $"{key} must be a positive integer."; return false; } return true; }
}
public static class UsageText
{
    public const string Value = """
Usage:
  probe assistant --text=<natural-language-instruction> [options]
  probe assistant-interpret --text=<natural-language-instruction> [options]
  probe assistant-run --text=<natural-language-instruction> --execution-profile=round4-2c-readonly --execute=true [options]
  probe context [options]
  probe export-annotations [options]
  probe move-annotation [options]
  probe run-all [options]
  probe plan-annotation-layout [options]
  probe detect-geometry-objects [options]
  probe plan-geometry-object-labels [options]
  probe apply-geometry-object-label-layout [options]
  probe audit-geometry-object-labels [options]

Options:
  --adapter=mock|file-bridge
  --bridge-root=<path>
  --timeout-ms=<milliseconds>
  --poll-interval-ms=<milliseconds>
  --allow-write=true
  --confirm-write=true
  --text=<natural-language-instruction>
  --base-url=<OpenAI-compatible base URL>
  --model=<model-id>
  --execute=true
  --execution-profile=round4-2c-readonly
  --snapshot=<snapshot.json>
  --output=<annotation-layout-plan.json>
  --plan=<geometry-object-label-plan.json>
  --snapshot=<geometry-object-detection-response.json>
  --labels=<geometry-label-inspection-response.json>
  --help, -h

Examples:
  probe assistant --text="识别当前图纸中的目标对象" --adapter=mock
  probe assistant-interpret --text="把所有法兰高亮出来" --base-url=https://api.yygu.cn/v3/llm.chat --model=deepseek/deepseek-v4-pro
  probe assistant --text="创建缺失的对象标签" --adapter=file-bridge --confirm-write=true --allow-write=true
  probe context --adapter=mock
  probe context --adapter=file-bridge --bridge-root=C:\AM_TribonBridge --timeout-ms=120000
  probe move-annotation --adapter=file-bridge ... --allow-write=true

No command is supplied by default: run-all is never implicit and a command must be explicit.
assistant only executes tasks from the controlled whitelist.
assistant-interpret performs model interpretation and planning only; it never calls Tribon.
Assistant model configuration uses ASSISTANT_BASE_URL, ASSISTANT_API_KEY, and ASSISTANT_MODEL. API keys are never accepted on the command line.
assistant drawing-write tasks require both --confirm-write=true and --allow-write=true.
Assistant tasks never execute SAVEWORK automatically.
File-bridge write operations require --allow-write=true.
plan-annotation-layout only generates a plan; it never applies drawing changes and does not require --allow-write=true.
detect-geometry-objects, plan-geometry-object-labels and audit-geometry-object-labels are read-only.
apply-geometry-object-label-layout requires --allow-write=true and never saves automatically.
AM_TRIBON_BRIDGE_ROOT may provide the bridge root when --bridge-root is omitted.
""";
}

public sealed class ProbeRunner(ITribonAdapter adapter)
{
    public async Task RunAllAsync(CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow; Directory.CreateDirectory("logs"); var logPath = Path.Combine("logs", $"probe-{started:yyyyMMdd}.log"); await File.AppendAllTextAsync(logPath, $"{started:O} INF Probe started{Environment.NewLine}", cancellationToken); await adapter.GetContextAsync(cancellationToken); Console.WriteLine("Context: succeeded");
        var export = await adapter.ExportAnnotationsAsync(new AnnotationExportRequest(), cancellationToken); Console.WriteLine($"Export annotations: succeeded ({export.Annotations.Count})"); var selected = export.Annotations.First(); var desired = new Point2D(selected.Position.X + 10, selected.Position.Y + 10); var moved = await adapter.MoveAnnotationAsync(new MoveAnnotationRequest { ObjectRef = selected.ObjectRef, ExpectedPosition = selected.Position, DesiredPosition = desired }, cancellationToken); Console.WriteLine("Move annotation: succeeded"); Console.WriteLine($"Refresh: {(moved.RefreshSucceeded ? "succeeded" : "failed")}"); var validation = await adapter.ValidateAnnotationAsync(new AnnotationValidationRequest(selected.ObjectRef, desired, 0.01), cancellationToken); Console.WriteLine($"Validation: {(validation.Succeeded ? "succeeded" : "failed")}");
    }
}
