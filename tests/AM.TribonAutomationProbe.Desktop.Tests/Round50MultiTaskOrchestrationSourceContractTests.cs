using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Xunit;

namespace AM.TribonAutomationProbe.Desktop.Tests;

public sealed class Round50MultiTaskOrchestrationSourceContractTests
{
    private const string ExpectedExecutorSha256 =
        "9CB209497223181818B0732899BB22A87D3CDAB0143549E2C696B73BC3C84411";

    private const string ExpectedPlannerSha256 =
        "AA13720EE7693D8ADAD982238F6B5E1140342342C985B20D68C162E87E9EF9CE";

    [Fact]
    public void ExecutorProductMessagingAndPlannerSourceRemainExact()
    {
        var repoRoot = FindRepoRoot();

        Assert.Equal(
            ExpectedExecutorSha256,
            Sha256(
                Path.Combine(
                    repoRoot,
                    "src",
                    "AM.TribonAutomationProbe.Desktop",
                    "Services",
                    "ConsoleAssistantReadOnlyPlanExecutionClient.cs")));

        Assert.Equal(
            ExpectedPlannerSha256,
            Sha256(
                Path.Combine(
                    repoRoot,
                    "src",
                    "AM.TribonAutomationProbe.Core",
                    "AssistantTaskPlanner.cs")));
    }

    [Fact]
    public void ViewModelSplitsThePlanIntoValidatedSingleTaskExecutions()
    {
        var source = ReadSource(
            "src",
            "AM.TribonAutomationProbe.Desktop",
            "ViewModels",
            "AssistantConversationViewModel.cs");

        Assert.Contains(
            "TryGetExecutableReadOnlyTasks(out var tasks)",
            source);
        Assert.Contains(
            "if (!TryGetExecutableReadOnlyTasks(out var tasks))",
            source);
        Assert.Contains(
            "tasks.Select(task => GetDisplayName(task.Intent))",
            source);
        Assert.Contains(
            "plan.Tasks.All(task =>",
            source);
        Assert.DoesNotContain(
            "TryGetExecutableReadOnlyTasks(out var task)",
            source);
        Assert.Contains(
            ".OrderBy(x => x.Sequence)",
            source);
        Assert.Contains(
            "candidate.Sequence != index + 1",
            source);
        Assert.Contains(
            "CreateSingleTaskReadOnlyPlan(",
            source);
        Assert.Contains(
            "Sequence = 1",
            source);
        Assert.Contains(
            "ConsoleAssistantReadOnlyPlanExecutionClient.ValidatePlan(",
            source);
        Assert.Contains(
            "await _readOnlyExecutionClient.ExecuteAsync(",
            source);
    }

    [Fact]
    public void ViewModelUsesSequentialStopOnFirstFailureReadOnlyPolicy()
    {
        var source = ReadSource(
            "src",
            "AM.TribonAutomationProbe.Desktop",
            "ViewModels",
            "AssistantConversationViewModel.cs");

        Assert.Contains(
            "for (var index = 0; index < tasks.Count; index++)",
            source);
        Assert.Contains(
            "cancellation.Token.ThrowIfCancellationRequested();",
            source);
        Assert.Contains(
            "result.DrawingWritePerformed ||",
            source);
        Assert.Contains(
            "result.SavePerformed)",
            source);
        Assert.Contains(
            "已停止，后续任务未提交",
            source);
        Assert.Contains(
            "全程未重新调用模型，未修改图纸，也未自动保存。",
            source);
    }

    [Fact]
    public void MainWindowExplainsAutomaticTribonExecutionChannelContract()
    {
        var source = ReadSource(
            "src",
            "AM.TribonAutomationProbe.Desktop",
            "MainWindow.xaml.cs");

        Assert.Contains(
            "即将按顺序执行当前",
            source);
        Assert.Contains(
            "系统会通过受控 Tribon 执行通道自动逐个处理",
            source);
        Assert.Contains(
            "任何任务失败或取消后立即停止",
            source);
        Assert.Contains(
            "不会提交后续任务",
            source);
        Assert.Contains(
            "执行阶段不会重新调用模型",
            source);
        Assert.Contains(
            "不会修改图纸，也不会自动保存",
            source);
        Assert.DoesNotContain(
            "按 Sequence 顺序执行当前",
            source);
        Assert.DoesNotContain(
            "Start.py",
            source);
        Assert.DoesNotContain(
            "单任务 Console 白名单执行器",
            source);
        Assert.DoesNotContain(
            "SAVEWORK",
            source);
    }

    private static string ReadSource(
        params string[] relativeSegments) =>
        File.ReadAllText(
            relativeSegments.Aggregate(
                FindRepoRoot(),
                (current, segment) =>
                    Path.Combine(
                        current,
                        segment)));

    private static string Sha256(string path) =>
        Convert.ToHexString(
            SHA256.HashData(
                File.ReadAllBytes(path)));

    private static string FindRepoRoot()
    {
        DirectoryInfo? current =
            new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        current.FullName,
                        "src",
                        "AM.TribonAutomationProbe.Core",
                        "AssistantTaskPlanner.cs")) &&
                File.Exists(
                    Path.Combine(
                        current.FullName,
                        "src",
                        "AM.TribonAutomationProbe.Desktop",
                        "ViewModels",
                        "AssistantConversationViewModel.cs")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Repository root could not be located from the test output directory.");
    }
}