using System.Security;
using Xunit;
using AM.TribonAutomationProbe.Core;
using AM.TribonAutomationProbe.Desktop.Models;
using AM.TribonAutomationProbe.Desktop.Services;
using AM.TribonAutomationProbe.Desktop.ViewModels;

namespace AM.TribonAutomationProbe.Desktop.Tests;

public sealed class AssistantConversationViewModelTests
{
        [Fact]
    public void MainWindow_ReadOnlyExecutionProgressBindings_AreExplicitlyOneWay()
    {
        var directory = new System.IO.DirectoryInfo(
            AppContext.BaseDirectory);

        while (directory is not null &&
               !System.IO.File.Exists(
                   System.IO.Path.Combine(
                       directory.FullName,
                       "AM.TribonAutomationProbe.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        var xamlPath = System.IO.Path.Combine(
            directory!.FullName,
            "src",
            "AM.TribonAutomationProbe.Desktop",
            "MainWindow.xaml");
        var xaml = System.IO.File.ReadAllText(xamlPath);

        Assert.Contains(
            "Value=\"{Binding ReadOnlyExecutionProgress, Mode=OneWay}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsIndeterminate=\"{Binding IsReadOnlyExecutionProgressIndeterminate, Mode=OneWay}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Value=\"{Binding ReadOnlyExecutionProgress}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "IsIndeterminate=\"{Binding IsReadOnlyExecutionProgressIndeterminate}\"",
            xaml,
            StringComparison.Ordinal);
    }
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
        Assert.False(viewModel.CanExecuteReadOnlyPlan);
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
        Assert.True(viewModel.CanExecuteReadOnlyPlan);
        Assert.Contains(
            "固定白名单命令",
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

    [Fact]
    public async Task ReadOnlyPlan_ExecutesThroughDeterministicClient()
    {
        var execution = new FakeReadOnlyPlanExecutionClient();
        var viewModel = new AssistantConversationViewModel(
            new FakeAssistantWorkflowClient(
                ConsoleAssistantWorkflowClientTests.CreateEnvelope(
                    AssistantIntent.HighlightFlanges)),
            new ObjectLabelWorkflowViewModel(
                new FakeLabelWorkflowClient()),
            execution)
        {
            UserInput = "高亮法兰"
        };

        await viewModel.InterpretAsync();

        Assert.True(viewModel.CanExecuteReadOnlyPlan);
        Assert.Contains(
            "法兰",
            viewModel.ReadOnlyExecutionButtonText,
            StringComparison.Ordinal);

        await viewModel.ExecuteReadOnlyPlanAsync();

        Assert.Equal(1, execution.CallCount);
        Assert.NotNull(execution.LastPlan);
        Assert.True(viewModel.HasReadOnlyExecutionResult);
        Assert.False(
            viewModel.ReadOnlyExecutionResult!.DrawingWritePerformed);
        Assert.False(viewModel.ReadOnlyExecutionResult.SavePerformed);
        Assert.Contains(
            "高亮完成",
            viewModel.ReadOnlyExecutionSummary,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RealModelSession_PassesSecureCredentialWithoutStoringIt()
    {
        var assistant = new FakeAssistantWorkflowClient(
            ConsoleAssistantWorkflowClientTests.CreateEnvelope(
                AssistantIntent.PreflightLabels));
        var viewModel = new AssistantConversationViewModel(
            assistant,
            new ObjectLabelWorkflowViewModel(
                new FakeLabelWorkflowClient()))
        {
            UseRealModel = true,
            AssistantBaseUrl =
                "https://example.test/chat/completions",
            AssistantModel = "provider-model",
            UserInput = "检查对象标签"
        };

        using var secret = new SecureString();

        foreach (var character in "session-token")
        {
            secret.AppendChar(character);
        }

        secret.MakeReadOnly();

        await viewModel.InterpretAsync(secret);

        Assert.NotNull(assistant.LastProviderSettings);
        Assert.Equal(
            AssistantProviderMode.OpenAiCompatible,
            assistant.LastProviderSettings!.Mode);
        Assert.Equal(
            "provider-model",
            assistant.LastProviderSettings.Model);
        Assert.True(assistant.LastAuthorizationSecretPresent);
        Assert.True(viewModel.HasPlan);
    }

    private sealed class FakeAssistantWorkflowClient(
        AssistantInterpretationEnvelope result) : IAssistantWorkflowClient
    {
        public AssistantProviderSessionSettings? LastProviderSettings
        {
            get;
            private set;
        }

        public bool LastAuthorizationSecretPresent
        {
            get;
            private set;
        }

        public Task<AssistantInterpretationEnvelope> InterpretAsync(
            ConsoleWorkflowSettings settings,
            AssistantProviderSessionSettings providerSettings,
            SecureString? authorizationSecret,
            string userText,
            CancellationToken cancellationToken)
        {
            LastProviderSettings = providerSettings;
            LastAuthorizationSecretPresent =
                authorizationSecret is { Length: > 0 };

            var envelope = result with
            {
                Plan = result.Plan with
                {
                    UserText = userText
                },
                Interpretation =
                    providerSettings.Mode ==
                    AssistantProviderMode.OpenAiCompatible
                        ? result.Interpretation with
                        {
                            Provider = "openai-compatible-chat",
                            Model = providerSettings.Model
                        }
                        : result.Interpretation
            };

            return Task.FromResult(envelope);
        }
    }

    private sealed class FakeReadOnlyPlanExecutionClient :
        IAssistantReadOnlyPlanExecutionClient
    {
        public int CallCount { get; private set; }

        public AssistantTaskPlan? LastPlan { get; private set; }

        public Task<AssistantTaskExecutionResult> ExecuteAsync(
            ConsoleWorkflowSettings settings,
            AssistantTaskPlan plan,
            IProgress<WorkflowProgress>? progress,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastPlan = plan;
            progress?.Report(
                new WorkflowProgress(100, "高亮完成"));

            return Task.FromResult(
                new AssistantTaskExecutionResult(
                    1,
                    "geometry.highlight-flanges",
                    AssistantTaskState.Completed,
                    "succeeded",
                    DrawingWritePerformed: false,
                    SavePerformed: false,
                    Summary: "高亮完成：2 个对象。"));
        }
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
