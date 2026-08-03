namespace AM.TribonAutomationProbe.Core;

public sealed class AssistantTaskPlanner
{
    private static readonly IReadOnlyDictionary<AssistantIntent, AssistantTaskDefinition> Definitions =
        new Dictionary<AssistantIntent, AssistantTaskDefinition>
        {
            [AssistantIntent.DetectGeometry] = new(
                "geometry.detect",
                AssistantTaskRisk.ReadOnly,
                false),
            [AssistantIntent.HighlightLifting] = new(
                "geometry.highlight-lifting",
                AssistantTaskRisk.ReadOnly,
                false),
            [AssistantIntent.HighlightFlanges] = new(
                "geometry.highlight-flanges",
                AssistantTaskRisk.ReadOnly,
                false),
            [AssistantIntent.ClearHighlight] = new(
                "geometry.highlight-clear",
                AssistantTaskRisk.ReadOnly,
                false),
            [AssistantIntent.PreflightLabels] = new(
                "geometry.label-preflight",
                AssistantTaskRisk.ReadOnly,
                false),
            [AssistantIntent.ApplyMissingLabels] = new(
                "geometry.label-apply-missing",
                AssistantTaskRisk.DrawingWrite,
                true)
        };

    public AssistantTaskPlan CreatePlan(
        AssistantConversationContext context,
        AssistantInterpretation interpretation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(interpretation);

        var planId = "PLAN-" + Guid.NewGuid().ToString("N");
        var createdAt = DateTimeOffset.UtcNow;

        if (interpretation.ClarificationRequired)
        {
            return new AssistantTaskPlan(
                SchemaVersion: "1.0",
                PlanId: planId,
                UserText: context.UserText,
                CreatedAt: createdAt,
                Tasks: Array.Empty<AssistantPlannedTask>(),
                RequiresConfirmation: false,
                ContainsWrite: false,
                AutoSave: false,
                State: AssistantTaskState.AwaitingClarification,
                Message: interpretation.ClarificationQuestion ??
                    "需要进一步明确要执行的任务。");
        }

        var planned = new List<AssistantPlannedTask>();

        foreach (var item in interpretation.Tasks)
        {
            if (!double.IsFinite(item.Confidence) || item.Confidence < 0.75)
            {
                return new AssistantTaskPlan(
                    SchemaVersion: "1.0",
                    PlanId: planId,
                    UserText: context.UserText,
                    CreatedAt: createdAt,
                    Tasks: Array.Empty<AssistantPlannedTask>(),
                    RequiresConfirmation: false,
                    ContainsWrite: false,
                    AutoSave: false,
                    State: AssistantTaskState.AwaitingClarification,
                    Message: "模型意图置信度不足，未执行任何 Tribon 操作。");
            }

            if (!Definitions.TryGetValue(item.Intent, out var definition))
            {
                return new AssistantTaskPlan(
                    SchemaVersion: "1.0",
                    PlanId: planId,
                    UserText: context.UserText,
                    CreatedAt: createdAt,
                    Tasks: Array.Empty<AssistantPlannedTask>(),
                    RequiresConfirmation: false,
                    ContainsWrite: false,
                    AutoSave: false,
                    State: AssistantTaskState.AwaitingClarification,
                    Message: "模型返回了未注册的任务，未执行任何 Tribon 操作。");
            }

            var arguments = item.Arguments is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : item.Arguments.ToDictionary(
                    x => x.Key,
                    x => x.Value,
                    StringComparer.Ordinal);

            arguments["scope"] = "current_drafting_context";

            planned.Add(
                new AssistantPlannedTask(
                    Sequence: planned.Count + 1,
                    Intent: item.Intent,
                    TaskType: definition.TaskType,
                    Risk: definition.Risk,
                    RequiresConfirmation: definition.RequiresConfirmation,
                    AutoSave: false,
                    Arguments: arguments));
        }

        if (planned.Count == 0)
        {
            return new AssistantTaskPlan(
                SchemaVersion: "1.0",
                PlanId: planId,
                UserText: context.UserText,
                CreatedAt: createdAt,
                Tasks: Array.Empty<AssistantPlannedTask>(),
                RequiresConfirmation: false,
                ContainsWrite: false,
                AutoSave: false,
                State: AssistantTaskState.AwaitingClarification,
                Message: "没有生成可执行的白名单任务。");
        }

        var containsWrite = planned.Any(x => x.Risk == AssistantTaskRisk.DrawingWrite);
        var requiresConfirmation = planned.Any(x => x.RequiresConfirmation);

        return new AssistantTaskPlan(
            SchemaVersion: "1.0",
            PlanId: planId,
            UserText: context.UserText,
            CreatedAt: createdAt,
            Tasks: planned,
            RequiresConfirmation: requiresConfirmation,
            ContainsWrite: containsWrite,
            AutoSave: false,
            State: requiresConfirmation
                ? AssistantTaskState.AwaitingConfirmation
                : AssistantTaskState.Planned,
            Message: requiresConfirmation
                ? "该计划包含图纸写入操作。确认后仅创建缺失标签，不自动执行 SAVEWORK。"
                : "任务计划已通过白名单和只读安全检查。");
    }

    private sealed record AssistantTaskDefinition(
        string TaskType,
        AssistantTaskRisk Risk,
        bool RequiresConfirmation);
}
