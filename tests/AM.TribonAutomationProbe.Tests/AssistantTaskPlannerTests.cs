using AM.TribonAutomationProbe.Core;
using Xunit;

namespace AM.TribonAutomationProbe.Tests;

public sealed class AssistantTaskPlannerTests
{
    private readonly AssistantTaskPlanner _planner = new();

    [Fact]
    public void ReadOnlyIntentProducesExecutablePlanWithoutConfirmation()
    {
        var plan = _planner.CreatePlan(
            new AssistantConversationContext("高亮所有法兰"),
            Interpretation(AssistantIntent.HighlightFlanges));

        Assert.Equal(AssistantTaskState.Planned, plan.State);
        Assert.False(plan.RequiresConfirmation);
        Assert.False(plan.ContainsWrite);
        Assert.False(plan.AutoSave);
        var task = Assert.Single(plan.Tasks);
        Assert.Equal("geometry.highlight-flanges", task.TaskType);
        Assert.Equal(AssistantTaskRisk.ReadOnly, task.Risk);
        Assert.False(task.RequiresConfirmation);
        Assert.False(task.AutoSave);
    }

    [Fact]
    public void LabelCreationRequiresConfirmationAndNeverAutoSaves()
    {
        var plan = _planner.CreatePlan(
            new AssistantConversationContext("创建缺失的对象标签"),
            Interpretation(AssistantIntent.ApplyMissingLabels));

        Assert.Equal(AssistantTaskState.AwaitingConfirmation, plan.State);
        Assert.True(plan.RequiresConfirmation);
        Assert.True(plan.ContainsWrite);
        Assert.False(plan.AutoSave);
        var task = Assert.Single(plan.Tasks);
        Assert.Equal("geometry.label-apply-missing", task.TaskType);
        Assert.Equal(AssistantTaskRisk.DrawingWrite, task.Risk);
        Assert.True(task.RequiresConfirmation);
        Assert.False(task.AutoSave);
        Assert.Contains("SAVEWORK", plan.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ClarificationResultProducesNoExecutableTasks()
    {
        var interpretation = new AssistantInterpretation(
            "test",
            "test",
            Array.Empty<AssistantInterpretedTask>(),
            true,
            "请明确操作。");

        var plan = _planner.CreatePlan(
            new AssistantConversationContext("处理一下"),
            interpretation);

        Assert.Equal(AssistantTaskState.AwaitingClarification, plan.State);
        Assert.Empty(plan.Tasks);
        Assert.False(plan.ContainsWrite);
    }


    [Fact]
    public void LowConfidenceModelIntentIsRejected()
    {
        var interpretation = new AssistantInterpretation(
            "test",
            "test",
            [new AssistantInterpretedTask(AssistantIntent.DetectGeometry, 0.4)],
            false);

        var plan = _planner.CreatePlan(
            new AssistantConversationContext("可能识别一下"),
            interpretation);

        Assert.Equal(AssistantTaskState.AwaitingClarification, plan.State);
        Assert.Empty(plan.Tasks);
        Assert.Contains("置信度", plan.Message, StringComparison.Ordinal);
    }

    [Fact] public void ConfidenceBoundaryAt075IsAccepted() { var plan = _planner.CreatePlan(new("高亮"), Interpretation(AssistantIntent.ClearHighlight, .75)); Assert.Equal(AssistantTaskState.Planned, plan.State); }
    [Fact] public void ConfidenceJustBelow075IsRejected() { var plan = _planner.CreatePlan(new("高亮"), Interpretation(AssistantIntent.ClearHighlight, .749999)); Assert.Equal(AssistantTaskState.AwaitingClarification, plan.State); }

    [Fact]
    public void UnsupportedModelIntentIsRejectedByWhitelist()
    {
        var plan = _planner.CreatePlan(
            new AssistantConversationContext("未知任务"),
            Interpretation(AssistantIntent.Unsupported));

        Assert.Equal(AssistantTaskState.AwaitingClarification, plan.State);
        Assert.Empty(plan.Tasks);
        Assert.Contains("未注册", plan.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompoundPlanPreservesSequenceAndRiskAggregation()
    {
        var interpretation = new AssistantInterpretation(
            "test",
            "test",
            [
                new AssistantInterpretedTask(AssistantIntent.DetectGeometry, 1.0),
                new AssistantInterpretedTask(AssistantIntent.ApplyMissingLabels, 1.0)
            ],
            false);

        var plan = _planner.CreatePlan(
            new AssistantConversationContext("识别并创建标签"),
            interpretation);

        Assert.Equal(2, plan.Tasks.Count);
        Assert.Equal(1, plan.Tasks[0].Sequence);
        Assert.Equal(2, plan.Tasks[1].Sequence);
        Assert.True(plan.ContainsWrite);
        Assert.True(plan.RequiresConfirmation);
        Assert.Equal(AssistantTaskState.AwaitingConfirmation, plan.State);
    }

    private static AssistantInterpretation Interpretation(AssistantIntent intent, double confidence = 1.0) =>
        new(
            "test",
            "test",
            [new AssistantInterpretedTask(intent, confidence)],
            false);
}
