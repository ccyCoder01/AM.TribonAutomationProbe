using System.IO;
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
        Assert.Contains(
            "Text=\"{Binding ExecutionStateText}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Text=\"{Binding ExecutionStatus}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Content=\"发送\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "ToolTip=\"Ctrl+Enter 发送任务\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Content=\"{Binding PlanExecutionButtonText}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Click=\"ExecutePlan_Click\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Content=\"执行标签只读检查\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Click=\"RunPreflight_Click\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Click=\"ExecuteReadOnlyPlan_Click\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Click=\"RunPlanPreflight_Click\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "本次会话使用真实模型",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "UseRealModel",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Header=\"{Binding ModelSettingsHeader}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "PasswordChanged=\"AssistantApiTokenBox_PasswordChanged\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Content=\"清除已保存 Token\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProductMessaging_HidesManualRuntimeImplementationDetails()
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

        var files = new[]
        {
            System.IO.Path.Combine(
                directory!.FullName,
                "src",
                "AM.TribonAutomationProbe.Desktop",
                "MainWindow.xaml"),
            System.IO.Path.Combine(
                directory.FullName,
                "src",
                "AM.TribonAutomationProbe.Desktop",
                "MainWindow.xaml.cs"),
            System.IO.Path.Combine(
                directory.FullName,
                "src",
                "AM.TribonAutomationProbe.Desktop",
                "ViewModels",
                "AssistantConversationViewModel.cs"),
            System.IO.Path.Combine(
                directory.FullName,
                "src",
                "AM.TribonAutomationProbe.Desktop",
                "ViewModels",
                "ObjectLabelWorkflowViewModel.cs"),
            System.IO.Path.Combine(
                directory.FullName,
                "src",
                "AM.TribonAutomationProbe.Desktop",
                "Services",
                "ConsoleAssistantReadOnlyPlanExecutionClient.cs"),
            System.IO.Path.Combine(
                directory.FullName,
                "src",
                "AM.TribonAutomationProbe.Desktop",
                "Services",
                "ConsoleWorkflowClient.cs")
        };

        foreach (var path in files)
        {
            var source = System.IO.File.ReadAllText(path);

            Assert.DoesNotContain(
                "Start.py",
                source,
                StringComparison.OrdinalIgnoreCase);
        }

        var xaml = System.IO.File.ReadAllText(files[0]);
        var codeBehind = System.IO.File.ReadAllText(files[1]);
        var conversation = System.IO.File.ReadAllText(files[2]);

        Assert.Contains(
            "Tribon 执行通道会自动处理只读检查和已授权的图纸修改",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Header=\"审计详情\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "确认创建标签",
            codeBehind,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Preflight ID:",
            codeBehind,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Plan Hash:",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains(
            "Tribon 执行通道保持安全停止状态",
            conversation,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Interpretation_ShowsAssistantFeedbackImmediatelyBeforeModelReturns()
    {
        var assistant = new DelayedAssistantWorkflowClient(
            ConsoleAssistantWorkflowClientTests.CreateEnvelope(
                AssistantIntent.HighlightFlanges));
        var viewModel = new AssistantConversationViewModel(
            assistant,
            new ObjectLabelWorkflowViewModel(
                new FakeLabelWorkflowClient()),
            new FakeReadOnlyPlanExecutionClient())
        {
            UserInput = "高亮法兰"
        };

        var pending = viewModel.InterpretAsync();

        await assistant.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(2));

        Assert.Contains(
            viewModel.Messages,
            message =>
                message.Role == "user" &&
                message.Content == "高亮法兰");
        Assert.Contains(
            viewModel.Messages,
            message =>
                message.Role == "assistant" &&
                message.Content == "正在理解你的指令…");
        Assert.Equal("正在理解", viewModel.ExecutionStateText);
        Assert.True(viewModel.CanCancel);

        assistant.Release.SetResult(true);
        await pending;

        Assert.DoesNotContain(
            viewModel.Messages,
            message => message.Content == "正在理解你的指令…");
        Assert.Equal("已完成", viewModel.ExecutionStateText);
    }

    [Fact]
    public void ConversationCentricUi_ShowsLiveStatusAndCollapsesSecondaryDetails()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !File.Exists(
                   Path.Combine(
                       directory.FullName,
                       "AM.TribonAutomationProbe.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        var xaml = File.ReadAllText(
            Path.Combine(
                directory!.FullName,
                "src",
                "AM.TribonAutomationProbe.Desktop",
                "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(
            Path.Combine(
                directory.FullName,
                "src",
                "AM.TribonAutomationProbe.Desktop",
                "MainWindow.xaml.cs"));

        Assert.Contains(
            "IsIndeterminate=\"{Binding IsBusy, Mode=OneWay}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Visibility=\"{Binding CanCancel, Converter={StaticResource BooleanToVisibilityConverter}}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Header=\"执行计划详情\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Header=\"{Binding ModelSettingsHeader}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Header=\"审计详情\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Content=\"创建标签\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsEnabled=\"{Binding CanCreateLabelsFromPreflight}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ApplyAcknowledged, Mode=TwoWay",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "准备修改图纸",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "workflow.ApplyAcknowledged = true;",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains(
            "workflow.ApplyAcknowledged = false;",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains(
            "Messages.CollectionChanged",
            codeBehind,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Interpretation_ExposesPlanningThenAutomaticallyExecutesReadOnlyPlan()
    {
        var assistant = new DelayedAssistantWorkflowClient(
            ConsoleAssistantWorkflowClientTests.CreateEnvelope(
                AssistantIntent.HighlightFlanges));
        var execution = new FakeReadOnlyPlanExecutionClient();
        var viewModel = new AssistantConversationViewModel(
            assistant,
            new ObjectLabelWorkflowViewModel(
                new FakeLabelWorkflowClient()),
            execution)
        {
            UserInput = "高亮法兰"
        };

        var pending = viewModel.InterpretAsync();

        await assistant.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(2));

        Assert.Equal(
            AssistantProductExecutionState.Planning,
            viewModel.ExecutionState);
        Assert.Contains(
            "理解",
            viewModel.ExecutionStatus,
            StringComparison.Ordinal);
        Assert.Equal(0, execution.CallCount);

        assistant.Release.SetResult(true);

        await pending;

        Assert.Equal(1, execution.CallCount);
        Assert.Equal(
            AssistantProductExecutionState.Completed,
            viewModel.ExecutionState);
        Assert.Contains(
            "只读任务已完成",
            viewModel.ExecutionStatus,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LabelApplyLifecycle_TransitionsThroughConfirmationAndCompletion()
    {
        var labelClient = new FakeLabelWorkflowClient();
        var labels = new ObjectLabelWorkflowViewModel(labelClient);
        var viewModel = new AssistantConversationViewModel(
            new FakeAssistantWorkflowClient(
                ConsoleAssistantWorkflowClientTests.CreateEnvelope(
                    AssistantIntent.ApplyMissingLabels)),
            labels)
        {
            UserInput = "创建缺失对象标签"
        };

        await viewModel.InterpretAsync();

        Assert.Equal(1, labelClient.PreflightCallCount);
        Assert.Equal(0, labelClient.ApplyCallCount);
        Assert.Equal(
            AssistantProductExecutionState.AwaitingWriteConfirmation,
            viewModel.ExecutionState);
        Assert.True(viewModel.IsAwaitingWriteConfirmation);

        labels.ApplyAcknowledged = true;
        var authorization =
            viewModel.CreateApplyAuthorizationFromPlan();

        Assert.NotNull(authorization);

        await viewModel.ExecuteCurrentPlanAsync(
            authorization);

        Assert.Equal(
            AssistantProductExecutionState.Completed,
            viewModel.ExecutionState);
        Assert.Contains(
            "尚未保存",
            viewModel.ExecutionStatus,
            StringComparison.Ordinal);
        Assert.True(viewModel.IsExecutionTerminal);
    }

    [Fact]
    public async Task ApplyIntent_UsesUnifiedPreflightThenBoundApplyRoute()
    {
        var assistant = new FakeAssistantWorkflowClient(
            ConsoleAssistantWorkflowClientTests.CreateEnvelope(
                AssistantIntent.ApplyMissingLabels));
        var labelClient = new FakeLabelWorkflowClient();
        var labels = new ObjectLabelWorkflowViewModel(
            labelClient);
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
        Assert.Equal(1, labelClient.PreflightCallCount);
        Assert.Equal(0, labelClient.ApplyCallCount);
        Assert.False(viewModel.CanExecuteReadOnlyPlan);
        Assert.True(labels.HasPreflight);
        Assert.True(labels.HasWritablePreflight);
        Assert.True(viewModel.ShowCreateLabelsFromPreflight);
        Assert.True(viewModel.CanCreateLabelsFromPreflight);
        Assert.Equal(
            AssistantPlanExecutionRoute.LabelApply,
            viewModel.PlanExecutionRoute);
        Assert.False(viewModel.CanExecuteCurrentPlan);
        Assert.False(viewModel.CanApplyFromPlan);

        labels.ApplyAcknowledged = true;

        Assert.True(viewModel.CanApplyFromPlan);
        Assert.True(viewModel.CanExecuteCurrentPlan);

        var authorization =
            viewModel.CreateApplyAuthorizationFromPlan();

        Assert.NotNull(authorization);
        Assert.True(authorization!.AllowWrite);
        Assert.True(authorization.WriteConfirmed);
        Assert.Equal(
            labels.PreflightResult!.OperationId,
            authorization.ConfirmedPreflightOperationId);
        Assert.Equal(
            labels.PreflightResult.PlanHash,
            authorization.ConfirmedPlanHash);
        Assert.Equal(
            labels.PreflightResult.ReadyOperationIds?.ToArray(),
            authorization.ConfirmedOperationIds?.ToArray());

        await viewModel.ExecuteCurrentPlanAsync(
            authorization);

        Assert.Equal(1, labelClient.ApplyCallCount);
        Assert.True(labels.HasApplyResult);
        Assert.False(labels.SavePerformed);
        Assert.False(viewModel.ShowCreateLabelsFromPreflight);
        Assert.False(viewModel.CanCreateLabelsFromPreflight);
        Assert.Equal(
            AssistantPlanExecutionRoute.None,
            viewModel.PlanExecutionRoute);
        Assert.False(viewModel.CanExecuteCurrentPlan);
        Assert.Contains(
            viewModel.Messages,
            message => message.Content.Contains(
                "标签创建完成",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyIntent_RejectsAuthorizationNotBoundToCurrentPreflight()
    {
        var labelClient = new FakeLabelWorkflowClient();
        var labels = new ObjectLabelWorkflowViewModel(
            labelClient);
        var viewModel = new AssistantConversationViewModel(
            new FakeAssistantWorkflowClient(
                ConsoleAssistantWorkflowClientTests.CreateEnvelope(
                    AssistantIntent.ApplyMissingLabels)),
            labels)
        {
            UserInput = "创建缺失对象标签"
        };

        await viewModel.InterpretAsync();
        labels.ApplyAcknowledged = true;

        var authorization =
            viewModel.CreateApplyAuthorizationFromPlan();

        Assert.NotNull(authorization);

        var tampered = authorization! with
        {
            ConfirmedPlanHash =
                new string('0', 64)
        };

        var error =
            await Assert.ThrowsAsync<System.IO.InvalidDataException>(
                () => viewModel.ExecuteCurrentPlanAsync(
                    tampered));

        Assert.Equal(
            "标签创建确认已失效；当前标签计划已发生变化，请重新检查后确认。",
            error.Message);
        Assert.Equal(0, labelClient.ApplyCallCount);
        Assert.False(labels.HasApplyResult);
    }

    [Fact]
    public async Task NonLabelReadOnlyPlan_AutomaticallyExecutes()
    {
        var assistant = new FakeAssistantWorkflowClient(
            ConsoleAssistantWorkflowClientTests.CreateEnvelope(
                AssistantIntent.HighlightFlanges));
        var execution = new FakeReadOnlyPlanExecutionClient();
        var viewModel = new AssistantConversationViewModel(
            assistant,
            new ObjectLabelWorkflowViewModel(
                new FakeLabelWorkflowClient()),
            execution)
        {
            UserInput = "高亮法兰"
        };

        await viewModel.InterpretAsync();

        Assert.True(viewModel.HasPlan);
        Assert.False(viewModel.PlanContainsWrite);
        Assert.Equal(1, execution.CallCount);
        Assert.True(viewModel.HasReadOnlyExecutionResult);
        Assert.False(viewModel.CanExecuteReadOnlyPlan);
        Assert.False(viewModel.CanExecuteCurrentPlan);
        Assert.Equal(
            AssistantProductExecutionState.Completed,
            viewModel.ExecutionState);
    }

    [Fact]
    public async Task Interpretation_AutomaticallyExecutesLabelPreflight()
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

        Assert.Equal(1, labelClient.PreflightCallCount);
        Assert.Equal(0, labelClient.ApplyCallCount);
        Assert.True(viewModel.LabelWorkflow.HasPreflight);
        Assert.Equal(
            AssistantProductExecutionState.Completed,
            viewModel.ExecutionState);
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

        Assert.Equal(1, execution.CallCount);
        Assert.False(viewModel.CanExecuteReadOnlyPlan);
        Assert.False(viewModel.CanExecuteCurrentPlan);
        Assert.Equal(
            AssistantProductExecutionState.Completed,
            viewModel.ExecutionState);
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
    public async Task ConfiguredModelSession_AlwaysUsesOpenAiCompatibleProvider()
    {
        var assistant = new FakeAssistantWorkflowClient(
            ConsoleAssistantWorkflowClientTests.CreateEnvelope(
                AssistantIntent.PreflightLabels));
        var viewModel = new AssistantConversationViewModel(
            assistant,
            new ObjectLabelWorkflowViewModel(
                new FakeLabelWorkflowClient()))
        {
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

    [Fact]
    public void ModelConfigurationStore_ProtectsCredentialAndRoundTripsForCurrentUser()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "AM-Tribon-R5.1F-C-Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var settingsPath = Path.Combine(
            directory,
            "assistant-model-settings.json");

        try
        {
            var store =
                new AssistantModelConfigurationStore(
                    settingsPath);

            using var secret = new SecureString();

            foreach (var character in "persistent-token")
            {
                secret.AppendChar(character);
            }

            secret.MakeReadOnly();

            store.Save(
                "https://example.test/chat/completions",
                "provider-model",
                secret);

            var snapshot = store.LoadSnapshot();

            Assert.True(snapshot.HasCredential);
            Assert.Equal(
                "https://example.test/chat/completions",
                snapshot.BaseUrl);
            Assert.Equal(
                "provider-model",
                snapshot.Model);

            var raw = File.ReadAllText(settingsPath);

            Assert.DoesNotContain(
                "persistent-token",
                raw,
                StringComparison.Ordinal);

            using var loaded =
                store.LoadCredential();

            Assert.NotNull(loaded);
            Assert.Equal(
                "persistent-token",
                SecureStringToString(loaded!));

            store.ClearCredential(
                snapshot.BaseUrl,
                snapshot.Model);

            Assert.False(
                store.LoadSnapshot().HasCredential);
        }
        finally
        {
            Directory.Delete(
                directory,
                recursive: true);
        }
    }

    [Fact]
    public void MainWindow_ModelCredentialIsNotClearedAfterInterpretation()
    {
        var directory = new DirectoryInfo(
            AppContext.BaseDirectory);

        while (directory is not null &&
               !File.Exists(
                   Path.Combine(
                       directory.FullName,
                       "AM.TribonAutomationProbe.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        var codeBehindPath = Path.Combine(
            directory!.FullName,
            "src",
            "AM.TribonAutomationProbe.Desktop",
            "MainWindow.xaml.cs");

        var code = File.ReadAllText(
            codeBehindPath);

        Assert.Contains(
            "AssistantModelConfigurationStore",
            code,
            StringComparison.Ordinal);
        Assert.Contains(
            "_modelConfigurationStore.LoadCredential()",
            code,
            StringComparison.Ordinal);
        Assert.Contains(
            "_modelConfigurationStore.Save(",
            code,
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            code.Split(
                "AssistantApiTokenBox.Clear()",
                StringSplitOptions.None).Length - 1);
        Assert.Contains(
            "ClearAssistantToken_Click",
            code,
            StringComparison.Ordinal);
    }

    private static string SecureStringToString(
        SecureString value)
    {
        var pointer =
            System.Runtime.InteropServices.Marshal.SecureStringToBSTR(
                value);

        try
        {
            return System.Runtime.InteropServices.Marshal.PtrToStringBSTR(
                pointer);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ZeroFreeBSTR(
                pointer);
        }
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

    private sealed class DelayedAssistantWorkflowClient(
        AssistantInterpretationEnvelope result) : IAssistantWorkflowClient
    {
        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AssistantInterpretationEnvelope> InterpretAsync(
            ConsoleWorkflowSettings settings,
            AssistantProviderSessionSettings providerSettings,
            SecureString? authorizationSecret,
            string userText,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);

            return result with
            {
                Plan = result.Plan with
                {
                    UserText = userText
                }
            };
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
