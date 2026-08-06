using Xunit;
using AM.TribonAutomationProbe.Core;
using AM.TribonAutomationProbe.Desktop.Models;
using AM.TribonAutomationProbe.Desktop.Services;
using AM.TribonAutomationProbe.Desktop.ViewModels;

namespace AM.TribonAutomationProbe.Desktop.Tests;

public sealed class ObjectLabelWorkflowViewModelTests
{
    [Fact]
    public async Task Preflight_RequiresAcknowledgementBeforeApply()
    {
        var client = new FakeConsoleWorkflowClient();
        var viewModel =
            new ObjectLabelWorkflowViewModel(client);

        await viewModel.RunPreflightAsync();

        Assert.True(viewModel.HasWritablePreflight);
        Assert.False(viewModel.CanApply);
        Assert.Equal(
            2,
            viewModel.MissingCount);

        viewModel.ApplyAcknowledged = true;

        Assert.True(viewModel.CanApply);
    }

    [Fact]
    public async Task Apply_ShowsManualSaveGuidance()
    {
        var client = new FakeConsoleWorkflowClient();
        var viewModel =
            new ObjectLabelWorkflowViewModel(client);

        await viewModel.RunPreflightAsync();
        viewModel.ApplyAcknowledged = true;
        await viewModel.ApplyAsync();

        Assert.True(viewModel.HasApplyResult);
        Assert.True(viewModel.ManualSaveRequired);
        Assert.False(viewModel.SavePerformed);
        Assert.False(viewModel.CanApply);
        Assert.Equal(
            2,
            viewModel.CreatedCount);
        Assert.Contains(
            "File → Save",
            viewModel.ManualSaveGuidance,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangingBridgeRoot_InvalidatesPreflight()
    {
        var client = new FakeConsoleWorkflowClient();
        var viewModel =
            new ObjectLabelWorkflowViewModel(client);

        await viewModel.RunPreflightAsync();

        viewModel.BridgeRoot =
            @"C:\AM_TribonBridge\Changed";

        Assert.False(viewModel.HasPreflight);
        Assert.False(viewModel.CanApply);
        Assert.Contains(
            "重新执行只读检查",
            viewModel.StatusMessage,
            StringComparison.Ordinal);
    }

    private sealed class FakeConsoleWorkflowClient :
        IConsoleWorkflowClient
    {
        public Task<GeometryLabelPreflightResult>
            RunPreflightAsync(
                ConsoleWorkflowSettings settings,
                IProgress<WorkflowProgress>? progress,
                CancellationToken cancellationToken)
        {
            progress?.Report(
                new WorkflowProgress(
                    100,
                    "done"));

            return Task.FromResult(
                ConsoleWorkflowClientTests.CreatePreflight());
        }

        public Task<GeometryLabelApplyMissingResult>
            RunApplyAsync(
                ConsoleWorkflowSettings settings,
                GeometryLabelPreflightResult confirmedPreflight,
                IProgress<WorkflowProgress>? progress,
                CancellationToken cancellationToken)
        {
            progress?.Report(
                new WorkflowProgress(
                    100,
                    "done"));

            return Task.FromResult(
                ConsoleWorkflowClientTests.CreateApply());
        }
    }
}
