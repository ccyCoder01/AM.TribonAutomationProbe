using Xunit;
using AM.TribonAutomationProbe.Core;
using AM.TribonAutomationProbe.Desktop.Models;
using AM.TribonAutomationProbe.Desktop.Services;
using AM.TribonAutomationProbe.Desktop.ViewModels;

namespace AM.TribonAutomationProbe.Desktop.Tests;

public sealed class AssistantConversationViewModelTests
{
    [Fact]
    public async Task ApplyIntent_PreviewsThenUsesExistingPreflightGate()
    {
        var assistant = new FakeAssistantWorkflowClient(
            ConsoleAssistantWorkflowClientTests.CreateEnvelope(
                AssistantIntent.ApplyMissingLabels));
        var labels = new ObjectLabelWorkflowViewModel(
            new FakeLabelWorkflowClient());
        var viewModel = new AssistantConversationViewModel(
            assistant,
            labels)
        {
            UserInput = "创建缺失对象标签"
        };

        await viewModel.InterpretAsync();

        Assert.True(viewModel.HasPlan);
        Assert.True(viewModel.PlanContainsWrite);
        Assert.True(viewModel.PlanRequiresConfirmation);
        Assert.True(viewModel.CanRunLabelPreflightFromPlan);
        Assert.False(labels.HasPreflight);
        Assert.False(viewModel.CanApplyFromPlan);

        await viewModel.RunLabelPreflightFromPlanAsync();

        Assert.True(labels.HasWritablePreflight);
        Assert.False(viewModel.CanApplyFromPlan);

        labels.ApplyAcknowledged = true;

        Assert.True(viewModel.CanApplyFromPlan);
        Assert.Contains(
            viewModel.Messages,
            message => message.Content.Contains(
                "待创建 2 个",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task NonLabelPlan_RemainsPreviewOnly()
    {
        var assistant = new FakeAssistantWorkflowClient(
            ConsoleAssistantWorkflowClientTests.CreateEnvelope(
                AssistantIntent.HighlightFlanges));
        var viewModel = new AssistantConversationViewModel(
            assistant,
            new ObjectLabelWorkflowViewModel(
                new FakeLabelWorkflowClient()))
        {
            UserInput = "高亮法兰"
        };

        await viewModel.InterpretAsync();

        Assert.True(viewModel.HasPlan);
        Assert.False(viewModel.PlanContainsWrite);
        Assert.False(viewModel.CanRunLabelPreflightFromPlan);
        Assert.Contains(
            "不会从自然语言直接调用 Tribon",
            viewModel.PlanSafetySummary,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Interpretation_DoesNotExecuteLabelWorkflow()
    {
        var labelClient = new FakeLabelWorkflowClient();
        var viewModel = new AssistantConversationViewModel(
            new FakeAssistantWorkflowClient(
                ConsoleAssistantWorkflowClientTests.CreateEnvelope(
                    AssistantIntent.PreflightLabels)),
            new ObjectLabelWorkflowViewModel(labelClient))
        {
            UserInput = "检查对象标签"
        };

        await viewModel.InterpretAsync();

        Assert.Equal(0, labelClient.PreflightCallCount);
        Assert.Equal(0, labelClient.ApplyCallCount);
        Assert.True(viewModel.CanRunLabelPreflightFromPlan);
    }

    private sealed class FakeAssistantWorkflowClient(
        AssistantInterpretationEnvelope result) : IAssistantWorkflowClient
    {
        public Task<AssistantInterpretationEnvelope> InterpretAsync(
            ConsoleWorkflowSettings settings,
            string userText,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                result with
                {
                    Plan = result.Plan with
                    {
                        UserText = userText
                    }
                });
    }

    private sealed class FakeLabelWorkflowClient : IConsoleWorkflowClient
    {
        public int PreflightCallCount { get; private set; }

        public int ApplyCallCount { get; private set; }

        public Task<GeometryLabelPreflightResult> RunPreflightAsync(
            ConsoleWorkflowSettings settings,
            IProgress<WorkflowProgress>? progress,
            CancellationToken cancellationToken)
        {
            PreflightCallCount++;
            progress?.Report(new WorkflowProgress(100, "done"));
            return Task.FromResult(
                ConsoleWorkflowClientTests.CreatePreflight());
        }

        public Task<GeometryLabelApplyMissingResult> RunApplyAsync(
            ConsoleWorkflowSettings settings,
            GeometryLabelPreflightResult confirmedPreflight,
            IProgress<WorkflowProgress>? progress,
            CancellationToken cancellationToken)
        {
            ApplyCallCount++;
            progress?.Report(new WorkflowProgress(100, "done"));
            return Task.FromResult(
                ConsoleWorkflowClientTests.CreateApply());
        }
    }
}
