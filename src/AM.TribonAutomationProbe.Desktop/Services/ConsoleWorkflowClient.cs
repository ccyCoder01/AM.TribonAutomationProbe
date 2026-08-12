using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using AM.TribonAutomationProbe.Core;
using AM.TribonAutomationProbe.Desktop.Models;

namespace AM.TribonAutomationProbe.Desktop.Services;

public sealed class ConsoleWorkflowClient : IConsoleWorkflowClient
{
    private readonly BridgeResultMonitor _bridgeMonitor;

    public ConsoleWorkflowClient(BridgeResultMonitor? bridgeMonitor = null)
    {
        _bridgeMonitor = bridgeMonitor ?? new BridgeResultMonitor();
    }

    public async Task<GeometryLabelPreflightResult> RunPreflightAsync(
        ConsoleWorkflowSettings settings,
        IProgress<WorkflowProgress>? progress,
        CancellationToken cancellationToken)
    {
        settings.Validate();
        _bridgeMonitor.EnsureIdle(settings.BridgeRoot);

        progress?.Report(
            new WorkflowProgress(
                10,
                "正在校验标签安全检查环境。"));

        var arguments = BuildPreflightArguments(settings);
        var result = await RunConsoleAsync<GeometryLabelPreflightResult>(
                settings.ConsolePath,
                arguments,
                "标签安全检查已提交，正在等待 Tribon 执行通道返回结果。",
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(
            new WorkflowProgress(
                90,
                "正在验证标签安全检查结果。"));

        ValidatePreflightResult(result);

        progress?.Report(
            new WorkflowProgress(
                100,
                "标签安全检查完成。"));

        return result;
    }

    public async Task<GeometryLabelApplyMissingResult> RunApplyAsync(
        ConsoleWorkflowSettings settings,
        GeometryLabelPreflightResult confirmedPreflight,
        IProgress<WorkflowProgress>? progress,
        CancellationToken cancellationToken)
    {
        settings.Validate();
        ValidatePreflightResult(confirmedPreflight);

        if (!string.Equals(
                confirmedPreflight.Status,
                "SUCCESS",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "只有成功的标签安全检查才能授权创建标签。");
        }

        if (confirmedPreflight.PreMissingCount <= 0)
        {
            throw new InvalidOperationException(
                "已确认的标签安全检查没有待创建标签。");
        }

        _bridgeMonitor.EnsureIdle(settings.BridgeRoot);

        progress?.Report(
            new WorkflowProgress(
                10,
                "正在校验写入授权与计划绑定。"));

        var arguments = BuildApplyArguments(
            settings,
            confirmedPreflight);

        var result = await RunConsoleAsync<GeometryLabelApplyMissingResult>(
                settings.ConsolePath,
                arguments,
                "标签创建请求已提交，正在等待 Tribon 执行通道返回结果。",
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(
            new WorkflowProgress(
                90,
                "正在验证写入回执。"));

        ValidateApplyResult(
            result,
            confirmedPreflight);

        progress?.Report(
            new WorkflowProgress(
                100,
                "标签创建完成，图纸尚未保存。"));

        return result;
    }

    public static IReadOnlyList<string> BuildPreflightArguments(
        ConsoleWorkflowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new[]
        {
            "preflight-object-labels",
            "--adapter=file-bridge",
            $"--bridge-root={Path.GetFullPath(settings.BridgeRoot)}",
            $"--timeout-ms={settings.TimeoutMs}",
            $"--poll-interval-ms={settings.PollIntervalMs}"
        };
    }

    public static IReadOnlyList<string> BuildApplyArguments(
        ConsoleWorkflowSettings settings,
        GeometryLabelPreflightResult confirmedPreflight)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(confirmedPreflight);

        ValidatePreflightResult(confirmedPreflight);

        var operationIds = confirmedPreflight.ReadyOperationIds ??
                           Array.Empty<string>();

        if (operationIds.Count == 0)
        {
            throw new InvalidOperationException(
                "已确认的标签安全检查没有可创建对象。");
        }

        return new[]
        {
            "apply-missing-object-labels",
            "--adapter=file-bridge",
            $"--bridge-root={Path.GetFullPath(settings.BridgeRoot)}",
            $"--timeout-ms={settings.TimeoutMs}",
            $"--poll-interval-ms={settings.PollIntervalMs}",
            "--allow-write=true",
            "--confirm-write=true",
            $"--confirmed-preflight-operation-id={confirmedPreflight.OperationId}",
            $"--confirmed-plan-hash={confirmedPreflight.PlanHash}",
            $"--confirmed-operation-ids={string.Join(',', operationIds)}"
        };
    }

    public static void ValidatePreflightResult(
        GeometryLabelPreflightResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!string.Equals(
                result.TaskType,
                "geometry.label-preflight",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "标签安全检查结果类型无效。");
        }

        if (result.DrawingWritePerformed)
        {
            throw new InvalidDataException(
                "标签安全检查异常报告了图纸写入。");
        }

        if (result.SavePerformed)
        {
            throw new InvalidDataException(
                "标签安全检查异常报告了图纸保存。");
        }

        if (result.Status is not ("SUCCESS" or "BLOCKED"))
        {
            throw new InvalidDataException(
                $"标签安全检查状态不受支持：{result.Status}");
        }

        if (string.IsNullOrWhiteSpace(result.OperationId))
        {
            throw new InvalidDataException(
                "标签安全检查缺少操作标识。");
        }

        if (!IsSha256(result.PlanHash))
        {
            throw new InvalidDataException(
                "标签安全检查缺少有效的计划校验值。");
        }

        var items = result.Items ??
                    throw new InvalidDataException(
                        "标签安全检查缺少对象明细。");

        var readyFromItems = items
            .Where(x => string.Equals(
                x.Decision,
                "READY_TO_CREATE",
                StringComparison.Ordinal))
            .Select(x => x.OperationId)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var readyFromResult = (
                result.ReadyOperationIds ??
                Array.Empty<string>())
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        if (readyFromItems.Any(string.IsNullOrWhiteSpace) ||
            readyFromItems.Distinct(StringComparer.Ordinal).Count() !=
                readyFromItems.Length)
        {
            throw new InvalidDataException(
                "标签安全检查中的待创建对象标识为空或重复。");
        }

        if (!readyFromItems.SequenceEqual(
                readyFromResult,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "标签安全检查的待创建对象集合与检查决策不一致。");
        }

        if (result.PreMissingCount != readyFromItems.Length)
        {
            throw new InvalidDataException(
                "标签安全检查的待创建数量与对象集合不一致。");
        }

        var alreadyAppliedCount = items.Count(
            x => string.Equals(
                x.Decision,
                "ALREADY_APPLIED",
                StringComparison.Ordinal));

        if (result.PreAlreadyPresentCount != alreadyAppliedCount)
        {
            throw new InvalidDataException(
                "标签安全检查的已存在数量与对象决策不一致。");
        }

        if (result.PreAlreadyPresentCount < 0 ||
            result.PreMissingCount < 0 ||
            result.PreDuplicateTextCount < 0 ||
            result.PreTextConflictCount < 0 ||
            result.PreInspectionErrorCount < 0)
        {
            throw new InvalidDataException(
                "标签安全检查数量不能为负数。");
        }

        if (string.Equals(
                result.Status,
                "SUCCESS",
                StringComparison.Ordinal) &&
            (result.PreDuplicateTextCount > 0 ||
             result.PreTextConflictCount > 0 ||
             result.PreInspectionErrorCount > 0))
        {
            throw new InvalidDataException(
                "标签安全检查标记成功，但仍包含阻断问题。");
        }
    }

    public static void ValidateApplyResult(
        GeometryLabelApplyMissingResult result,
        GeometryLabelPreflightResult confirmedPreflight)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(confirmedPreflight);

        if (!string.Equals(
                result.TaskType,
                "geometry.label-apply-missing",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "标签创建结果类型无效。");
        }

        if (result.SavePerformed)
        {
            throw new InvalidDataException(
                "标签创建异常执行了图纸保存。");
        }

        if (result.Status is not ("SUCCESS" or "ALREADY_COMPLETE"))
        {
            throw new InvalidDataException(
                $"标签创建状态不受支持：{result.Status}");
        }

        var confirmed = (
                confirmedPreflight.ReadyOperationIds ??
                Array.Empty<string>())
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var created = (
                result.CreatedOperationIds ??
                Array.Empty<string>())
            .ToArray();

        var failed = (
                result.FailedOperationIds ??
                Array.Empty<string>())
            .ToArray();

        var completed = created
            .Concat(failed)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        if (created.Distinct(StringComparer.Ordinal).Count() !=
                created.Length ||
            failed.Distinct(StringComparer.Ordinal).Count() !=
                failed.Length ||
            created.Intersect(
                    failed,
                    StringComparer.Ordinal)
                .Any())
        {
            throw new InvalidDataException(
                "标签创建结果中的对象标识重复或重叠。");
        }

        if (!completed.SequenceEqual(
                confirmed,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "标签创建结果与已确认的对象集合不一致。");
        }

        if (result.CreatedCount != created.Length ||
            result.CreateFailedCount != failed.Length ||
            result.CreatedRuntimeHandles.Count != created.Length ||
            result.DrawingWriteCount != created.Length ||
            result.DrawingWritePerformed != (created.Length > 0))
        {
            throw new InvalidDataException(
                "标签创建数量与图纸写入回执不一致。");
        }

        if (result.PostMissingCount != 0)
        {
            throw new InvalidDataException(
                "标签创建完成后仍存在缺失标签。");
        }
    }

    private static async Task<T> RunConsoleAsync<T>(
        string consolePath,
        IReadOnlyList<string> arguments,
        string waitingMessage,
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
                "正在连接 Tribon 执行通道。"));

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "Tribon 执行通道启动失败。");
        }

        progress?.Report(
            new WorkflowProgress(
                45,
                waitingMessage,
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

        if (string.IsNullOrWhiteSpace(standardOutput))
        {
            throw new InvalidDataException(
                "Tribon 执行通道未返回结果。");
        }

        progress?.Report(
            new WorkflowProgress(
                80,
                "Tribon 执行通道已返回结果，正在解析。"));

        try
        {
            return JsonSerializer.Deserialize<T>(
                       standardOutput.Trim(),
                       JsonDefaults.Options) ??
                   throw new InvalidDataException(
                       "Tribon 执行通道返回空结果。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Tribon 执行通道返回的结果格式无效。",
                ex);
        }
    }

    private static bool IsSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length != 64)
        {
            return false;
        }

        return value.All(
            x =>
                (x >= '0' && x <= '9') ||
                (x >= 'a' && x <= 'f') ||
                (x >= 'A' && x <= 'F'));
    }
}

public sealed class ConsoleWorkflowException : Exception
{
    public ConsoleWorkflowException(
        int exitCode,
        string standardError,
        string standardOutput)
        : base(BuildMessage(
            exitCode,
            standardError,
            standardOutput))
    {
        ExitCode = exitCode;
        StandardError = standardError;
        StandardOutput = standardOutput;
    }

    public int ExitCode { get; }

    public string StandardError { get; }

    public string StandardOutput { get; }

    private static string BuildMessage(
        int exitCode,
        string standardError,
        string standardOutput)
    {
        var detail = !string.IsNullOrWhiteSpace(standardError)
            ? standardError.Trim()
            : standardOutput.Trim();

        if (detail.Length > 1200)
        {
            detail = detail[..1200];
        }

        return $"Tribon 执行通道返回错误（代码 {exitCode}）：{detail}";
    }
}
