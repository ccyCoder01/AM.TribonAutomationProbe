using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using AM.TribonAutomationProbe.Core;
using AM.TribonAutomationProbe.Desktop.Models;

namespace AM.TribonAutomationProbe.Desktop.Services;

public sealed class ConsoleAssistantReadOnlyPlanExecutionClient :
    IAssistantReadOnlyPlanExecutionClient
{
    private static readonly IReadOnlyDictionary<AssistantIntent, ExecutionDefinition>
        Definitions = new Dictionary<AssistantIntent, ExecutionDefinition>
        {
            [AssistantIntent.DetectGeometry] = new(
                "detect-geometry",
                "geometry.detect"),
            [AssistantIntent.HighlightLifting] = new(
                "highlight-lifting",
                "geometry.highlight-lifting"),
            [AssistantIntent.HighlightFlanges] = new(
                "highlight-flanges",
                "geometry.highlight-flanges"),
            [AssistantIntent.ClearHighlight] = new(
                "clear-highlight",
                "geometry.highlight-clear")
        };

    private static readonly string[] ModelEnvironmentVariables =
    {
        "ASSISTANT_BASE_URL",
        "ASSISTANT_API_KEY",
        "ASSISTANT_MODEL"
    };

    private readonly BridgeResultMonitor _bridgeMonitor;

    public ConsoleAssistantReadOnlyPlanExecutionClient(
        BridgeResultMonitor? bridgeMonitor = null)
    {
        _bridgeMonitor = bridgeMonitor ?? new BridgeResultMonitor();
    }

    public async Task<AssistantTaskExecutionResult> ExecuteAsync(
        ConsoleWorkflowSettings settings,
        AssistantTaskPlan plan,
        IProgress<WorkflowProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(plan);

        settings.Validate();
        var task = ValidatePlan(plan);
        _bridgeMonitor.EnsureIdle(settings.BridgeRoot);

        progress?.Report(
            new WorkflowProgress(
                10,
                "正在校验只读计划与 FileBridge 空闲状态。"));

        var arguments = BuildExecutionArguments(settings, plan);
        var standardOutput = await RunConsoleAsync(
                settings.ConsolePath,
                arguments,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(
            new WorkflowProgress(
                85,
                "已收到 Console 结果，正在执行只读回执校验。"));

        var result = ParseAndValidate(task, standardOutput);

        progress?.Report(
            new WorkflowProgress(
                100,
                result.Summary));

        return result;
    }

    public static IReadOnlyList<string> BuildExecutionArguments(
        ConsoleWorkflowSettings settings,
        AssistantTaskPlan plan)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var task = ValidatePlan(plan);
        var definition = Definitions[task.Intent];

        return new[]
        {
            definition.Command,
            "--adapter=file-bridge",
            $"--bridge-root={Path.GetFullPath(settings.BridgeRoot)}",
            $"--timeout-ms={settings.TimeoutMs}",
            $"--poll-interval-ms={settings.PollIntervalMs}"
        };
    }

    public static AssistantPlannedTask ValidatePlan(
        AssistantTaskPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!string.Equals(
                plan.SchemaVersion,
                "1.0",
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(plan.PlanId) ||
            plan.State != AssistantTaskState.Planned ||
            plan.Tasks.Count != 1 ||
            plan.ContainsWrite ||
            plan.RequiresConfirmation ||
            plan.AutoSave)
        {
            throw new InvalidDataException(
                "The plan is not an executable single read-only plan.");
        }

        var task = plan.Tasks[0];

        if (!Definitions.TryGetValue(task.Intent, out var definition) ||
            task.Sequence != 1 ||
            !string.Equals(
                task.TaskType,
                definition.TaskType,
                StringComparison.Ordinal) ||
            task.Risk != AssistantTaskRisk.ReadOnly ||
            task.RequiresConfirmation ||
            task.AutoSave ||
            task.Arguments is null ||
            !task.Arguments.TryGetValue("scope", out var scope) ||
            !string.Equals(
                scope,
                "current_drafting_context",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The planned task is outside the deterministic read-only execution whitelist.");
        }

        return task;
    }

    public static void ValidateDetectionResult(
        GeometryDetectionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ValidateCommon(
            result.SchemaVersion,
            result.TaskType,
            "geometry.detect",
            result.OperationId,
            result.DrawingContext,
            result.StartedAt,
            result.CompletedAt,
            result.Status,
            result.DrawingWritePerformed,
            result.SavePerformed);

        if (result.Objects is null ||
            result.Diagnostics is null ||
            result.Diagnostics.CapturedContourCount < 0 ||
            result.Diagnostics.AssignedUniqueContourCount < 0 ||
            result.Diagnostics.UnassignedContourCount < 0 ||
            result.Diagnostics.ConflictHandleCount < 0 ||
            result.Diagnostics.ParseFailureCount < 0 ||
            result.Objects.Any(x =>
                string.IsNullOrWhiteSpace(x.RuntimeObjectId)))
        {
            throw new InvalidDataException(
                "Geometry detection result contains invalid objects or diagnostics.");
        }
    }

    public static void ValidateHighlightResult(
        GeometryHighlightResult result,
        AssistantIntent expectedIntent)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (expectedIntent is not (
                AssistantIntent.HighlightLifting or
                AssistantIntent.HighlightFlanges))
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedIntent));
        }

        var expectedTaskType = Definitions[expectedIntent].TaskType;
        ValidateCommon(
            result.SchemaVersion,
            result.TaskType,
            expectedTaskType,
            result.OperationId,
            result.DrawingContext,
            result.StartedAt,
            result.CompletedAt,
            result.Status,
            result.DrawingWritePerformed,
            result.SavePerformed);

        if (result.HighlightedObjectCount < 0 ||
            result.HighlightedHandleCount < 0 ||
            result.HighlightSuccessCount < 0 ||
            result.MissingHandleCount < 0 ||
            result.HighlightFailureCount < 0 ||
            result.HighlightSuccessCount +
                result.MissingHandleCount +
                result.HighlightFailureCount !=
                result.HighlightedHandleCount ||
            result.Categories is null)
        {
            throw new InvalidDataException(
                "Geometry highlight result contains inconsistent counts.");
        }

        var expectedCategories = expectedIntent ==
                                 AssistantIntent.HighlightLifting
            ? new[]
            {
                GeometryObjectCategory.LIFTING_BEAM,
                GeometryObjectCategory.LIFTING_LUG
            }
            : new[]
            {
                GeometryObjectCategory.PIPE_FLANGE_FRONT,
                GeometryObjectCategory.PIPE_FLANGE_SIDE,
                GeometryObjectCategory.STRUCTURAL_FLANGE
            };

        if (!result.Categories
                .OrderBy(x => x)
                .SequenceEqual(expectedCategories.OrderBy(x => x)))
        {
            throw new InvalidDataException(
                "Geometry highlight categories do not match the planned intent.");
        }
    }

    public static void ValidateClearResult(
        GeometryHighlightClearResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ValidateCommon(
            result.SchemaVersion,
            result.TaskType,
            "geometry.highlight-clear",
            result.OperationId,
            result.DrawingContext,
            result.StartedAt,
            result.CompletedAt,
            result.Status,
            result.DrawingWritePerformed,
            result.SavePerformed);

        if (!result.Cleared)
        {
            throw new InvalidDataException(
                "The clear-highlight result did not confirm that highlight was cleared.");
        }
    }

    private static AssistantTaskExecutionResult ParseAndValidate(
        AssistantPlannedTask task,
        string standardOutput)
    {
        return task.Intent switch
        {
            AssistantIntent.DetectGeometry =>
                CreateDetectionResult(
                    task,
                    Deserialize<GeometryDetectionResult>(standardOutput)),
            AssistantIntent.HighlightLifting or
            AssistantIntent.HighlightFlanges =>
                CreateHighlightResult(
                    task,
                    Deserialize<GeometryHighlightResult>(standardOutput)),
            AssistantIntent.ClearHighlight =>
                CreateClearResult(
                    task,
                    Deserialize<GeometryHighlightClearResult>(standardOutput)),
            _ => throw new InvalidDataException(
                "The read-only task is not registered for deterministic execution.")
        };
    }

    private static AssistantTaskExecutionResult CreateDetectionResult(
        AssistantPlannedTask task,
        GeometryDetectionResult result)
    {
        ValidateDetectionResult(result);
        var summary =
            $"对象识别完成：{result.Objects.Count} 个对象，" +
            $"捕获轮廓 {result.Diagnostics.CapturedContourCount} 个，" +
            $"未分配 {result.Diagnostics.UnassignedContourCount} 个。";

        return CreateCompletedResult(task, result.Status, summary, result);
    }

    private static AssistantTaskExecutionResult CreateHighlightResult(
        AssistantPlannedTask task,
        GeometryHighlightResult result)
    {
        ValidateHighlightResult(result, task.Intent);
        var summary =
            $"高亮完成：{result.HighlightedObjectCount} 个对象，" +
            $"{result.HighlightSuccessCount}/{result.HighlightedHandleCount} 个轮廓成功，" +
            $"缺失 {result.MissingHandleCount} 个，失败 {result.HighlightFailureCount} 个。";

        return CreateCompletedResult(task, result.Status, summary, result);
    }

    private static AssistantTaskExecutionResult CreateClearResult(
        AssistantPlannedTask task,
        GeometryHighlightClearResult result)
    {
        ValidateClearResult(result);
        return CreateCompletedResult(
            task,
            result.Status,
            "当前图纸的临时高亮已清除。",
            result);
    }

    private static AssistantTaskExecutionResult CreateCompletedResult(
        AssistantPlannedTask task,
        string status,
        string summary,
        object rawResult) =>
        new(
            task.Sequence,
            task.TaskType,
            AssistantTaskState.Completed,
            status,
            DrawingWritePerformed: false,
            SavePerformed: false,
            Summary: summary,
            RawResult: rawResult);

    private static void ValidateCommon(
        string schemaVersion,
        string taskType,
        string expectedTaskType,
        string operationId,
        string drawingContext,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string status,
        bool drawingWritePerformed,
        bool savePerformed)
    {
        if (!string.Equals(
                schemaVersion,
                "1.0",
                StringComparison.Ordinal) ||
            !string.Equals(
                taskType,
                expectedTaskType,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(operationId) ||
            !string.Equals(
                drawingContext,
                "current_drafting_context",
                StringComparison.Ordinal) ||
            completedAt < startedAt ||
            !string.Equals(
                status,
                "succeeded",
                StringComparison.Ordinal) ||
            drawingWritePerformed ||
            savePerformed)
        {
            throw new InvalidDataException(
                "The deterministic read-only result failed its common safety contract.");
        }
    }

    private static T Deserialize<T>(string standardOutput)
    {
        if (string.IsNullOrWhiteSpace(standardOutput))
        {
            throw new InvalidDataException(
                "The verified Console returned no JSON result.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(
                       standardOutput.Trim(),
                       JsonDefaults.Options) ??
                   throw new InvalidDataException(
                       "The verified Console JSON result is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "The verified Console output is not valid JSON.",
                ex);
        }
    }

    private static async Task<string> RunConsoleAsync(
        string consolePath,
        IReadOnlyList<string> arguments,
        IProgress<WorkflowProgress>? progress,
        CancellationToken cancellationToken)
    {
        var fullConsolePath = Path.GetFullPath(consolePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = fullConsolePath,
            WorkingDirectory = Path.GetDirectoryName(fullConsolePath) ??
                               AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };

        foreach (var name in ModelEnvironmentVariables)
        {
            startInfo.Environment.Remove(name);
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo
        };

        progress?.Report(
            new WorkflowProgress(
                25,
                "正在启动已验证的 Console。"));

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "The verified Console process could not be started.");
        }

        progress?.Report(
            new WorkflowProgress(
                45,
                "Console 已提交固定只读命令。请在 Tribon 当前图纸中运行 Start.py 恰好一次。",
                IsIndeterminate: true));

        using var cancellationRegistration =
            cancellationToken.Register(
                () =>
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(
                                entireProcessTree: true);
                        }
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                    }
                });

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken)
            .ConfigureAwait(false);

        var standardOutput = await standardOutputTask
            .ConfigureAwait(false);
        var standardError = await standardErrorTask
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
        {
            throw new ConsoleWorkflowException(
                process.ExitCode,
                standardError,
                standardOutput);
        }

        if (!string.IsNullOrWhiteSpace(standardError))
        {
            throw new InvalidDataException(
                "The deterministic read-only Console wrote to stderr.");
        }

        return standardOutput;
    }

    private sealed record ExecutionDefinition(
        string Command,
        string TaskType);
}
