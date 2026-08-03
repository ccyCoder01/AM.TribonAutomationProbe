using System.Text.RegularExpressions;

namespace AM.TribonAutomationProbe.Core;

/// <summary>
/// Deterministic development fallback for Round 4.2A. It implements the same
/// structured contract that a hosted or private large language model adapter
/// must implement in later rounds.
/// </summary>
public sealed class RuleBasedAssistantLanguageModel : IAssistantLanguageModel
{
    private static readonly Regex ClauseSeparator = new(
        "(?:然后|随后|接着|并且|同时|[；;]|[,，]\\s*再)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Task<AssistantInterpretation> InterpretAsync(
        AssistantConversationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(context.UserText))
        {
            return Task.FromResult(
                Clarification(
                    "请输入要执行的船舶设计任务。",
                    "Input was empty."));
        }

        var clauses = ClauseSeparator
            .Split(context.UserText.Trim())
            .Select(Normalize)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        var interpreted = new List<AssistantInterpretedTask>();
        var unresolved = new List<string>();

        foreach (var clause in clauses)
        {
            var matches = ClassifyClause(clause);

            if (matches.Count == 0)
            {
                unresolved.Add(clause);
                continue;
            }

            foreach (var match in matches)
            {
                if (interpreted.Count == 0 || interpreted[^1].Intent != match.Intent)
                {
                    interpreted.Add(match);
                }
            }
        }

        if (interpreted.Count == 0)
        {
            var question = LooksAmbiguous(context.UserText)
                ? "需要明确要执行的操作：识别对象、高亮吊梁吊耳、高亮法兰、清除高亮、检查标签或创建缺失标签。"
                : "当前指令不在受控能力白名单内，请明确要执行的几何识别、高亮或对象标签任务。";

            return Task.FromResult(
                Clarification(
                    question,
                    "No supported task could be derived from the input."));
        }

        if (unresolved.Count > 0)
        {
            return Task.FromResult(
                new AssistantInterpretation(
                    Provider: "deterministic",
                    Model: "rule-based-v1",
                    Tasks: interpreted,
                    ClarificationRequired: true,
                    ClarificationQuestion: "指令中包含无法安全解释的部分，请拆分或明确后再执行。",
                    Explanation: "Unresolved clauses: " + string.Join(" | ", unresolved)));
        }

        return Task.FromResult(
            new AssistantInterpretation(
                Provider: "deterministic",
                Model: "rule-based-v1",
                Tasks: interpreted,
                ClarificationRequired: false,
                Explanation: "Mapped to the controlled assistant task whitelist."));
    }

    private static IReadOnlyList<AssistantInterpretedTask> ClassifyClause(string clause)
    {
        var result = new List<AssistantInterpretedTask>();

        var hasHighlight = ContainsAny(clause, "高亮", "突出显示", "突出", "亮显", "标亮");
        var hasClear = ContainsAny(clause, "清除", "取消", "关闭", "去掉", "移除") &&
                       ContainsAny(clause, "高亮", "亮显", "标亮");
        var hasLifting = ContainsAny(clause, "吊梁", "吊耳", "lifting beam", "lifting lug");
        var hasFlange = ContainsAny(clause, "法兰", "flange");
        var hasLabel = ContainsAny(clause, "对象标签", "标签", "标注", "label");
        var hasCreate = ContainsAny(clause, "创建", "补齐", "补全", "生成", "添加", "新建");
        var hasInspect = ContainsAny(clause, "检查", "核对", "审计", "查看", "有没有", "是否有");
        var hasDetect = ContainsAny(clause, "识别", "检测", "查找", "找出", "看看", "统计", "有多少", "多少个");
        var hasGeometryScope = ContainsAny(
            clause,
            "目标对象",
            "几何对象",
            "图纸对象",
            "当前图纸",
            "图纸里",
            "图纸中",
            "吊梁",
            "吊耳",
            "法兰");

        if (hasClear)
        {
            result.Add(CreateTask(AssistantIntent.ClearHighlight, 0.99));
            return result;
        }

        if (hasDetect && hasGeometryScope)
        {
            result.Add(CreateTask(AssistantIntent.DetectGeometry, 0.97));
        }

        if (hasHighlight && hasLifting)
        {
            result.Add(CreateTask(AssistantIntent.HighlightLifting, 0.98));
        }

        if (hasHighlight && hasFlange)
        {
            result.Add(CreateTask(AssistantIntent.HighlightFlanges, 0.98));
        }

        if (hasLabel && hasCreate)
        {
            result.Add(CreateTask(AssistantIntent.ApplyMissingLabels, 0.98));
        }
        else if (hasLabel && hasInspect)
        {
            result.Add(CreateTask(AssistantIntent.PreflightLabels, 0.97));
        }

        return result;
    }

    private static AssistantInterpretedTask CreateTask(
        AssistantIntent intent,
        double confidence) =>
        new(
            intent,
            confidence,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["scope"] = "current_drafting_context"
            });

    private static AssistantInterpretation Clarification(
        string question,
        string explanation) =>
        new(
            Provider: "deterministic",
            Model: "rule-based-v1",
            Tasks: Array.Empty<AssistantInterpretedTask>(),
            ClarificationRequired: true,
            ClarificationQuestion: question,
            Explanation: explanation);

    private static bool LooksAmbiguous(string value) =>
        ContainsAny(
            Normalize(value),
            "处理一下",
            "处理好",
            "改好",
            "优化一下",
            "整理一下",
            "自动处理",
            "帮我处理");

    private static string Normalize(string value) =>
        value
            .Trim()
            .ToLowerInvariant()
            .Replace("。", string.Empty, StringComparison.Ordinal)
            .Replace("！", string.Empty, StringComparison.Ordinal)
            .Replace("？", string.Empty, StringComparison.Ordinal)
            .Replace("!", string.Empty, StringComparison.Ordinal)
            .Replace("?", string.Empty, StringComparison.Ordinal);

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase));
}
