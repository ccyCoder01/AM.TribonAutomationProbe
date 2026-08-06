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
                "正在校验只读检查环境。"));

        var arguments = BuildPreflightArguments(settings);
        var result = await RunConsoleAsync<GeometryLabelPreflightResult>(
                settings.ConsolePath,
                arguments,
                "Console 已启动并正在提交只读检查请求。请在 Tribon 当前图纸中运行 Start.py 一次。",
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(
            new WorkflowProgress(
                90,
                "正在验证只读检查结果。"));

        ValidatePreflightResult(result);

        progress?.Report(
            new WorkflowProgress(
                100,
                "只读检查完成。"));

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
                "Only a successful preflight can authorize Apply.");
        }

        if (confirmedPreflight.PreMissingCount <= 0)
        {
            throw new InvalidOperationException(
                "The confirmed preflight contains no missing labels.");
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
                "Console 已启动并正在提交 Apply 请求。请在 Tribon 当前图纸中运行 Start.py 一次。",
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
                "Apply 完成，尚未自动保存。"));

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
                "The confirmed preflight has no ready operation IDs.");
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
                "Preflight taskType is invalid.");
        }

        if (result.DrawingWritePerformed)
        {
            throw new InvalidDataException(
                "Read-only preflight unexpectedly reported a drawing write.");
        }

        if (result.SavePerformed)
        {
            throw new InvalidDataException(
                "Read-only preflight unexpectedly reported a save.");
        }

        if (result.Status is not ("SUCCESS" or "BLOCKED"))
        {
            throw new InvalidDataException(
                $"Preflight status is unsupported: {result.Status}");
        }

        if (string.IsNullOrWhiteSpace(result.OperationId))
        {
            throw new InvalidDataException(
                "Preflight operationId is missing.");
        }

        if (!IsSha256(result.PlanHash))
        {
            throw new InvalidDataException(
                "Preflight planHash is not a SHA-256 value.");
        }

        var items = result.Items ??
                    throw new InvalidDataException(
                        "Preflight items are missing.");

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
                "Preflight ready operation IDs are blank or duplicated.");
        }

        if (!readyFromItems.SequenceEqual(
                readyFromResult,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "Preflight ready operation IDs do not match item decisions.");
        }

        if (result.PreMissingCount != readyFromItems.Length)
        {
            throw new InvalidDataException(
                "Preflight missing count does not match ready operations.");
        }

        var alreadyAppliedCount = items.Count(
            x => string.Equals(
                x.Decision,
                "ALREADY_APPLIED",
                StringComparison.Ordinal));

        if (result.PreAlreadyPresentCount != alreadyAppliedCount)
        {
            throw new InvalidDataException(
                "Preflight already-present count does not match item decisions.");
        }

        if (result.PreAlreadyPresentCount < 0 ||
            result.PreMissingCount < 0 ||
            result.PreDuplicateTextCount < 0 ||
            result.PreTextConflictCount < 0 ||
            result.PreInspectionErrorCount < 0)
        {
            throw new InvalidDataException(
                "Preflight counts cannot be negative.");
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
                "Successful preflight contains blocking diagnostics.");
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
                "Apply taskType is invalid.");
        }

        if (result.SavePerformed)
        {
            throw new InvalidDataException(
                "Apply unexpectedly performed SAVEWORK.");
        }

        if (result.Status is not ("SUCCESS" or "ALREADY_COMPLETE"))
        {
            throw new InvalidDataException(
                $"Apply status is unsupported: {result.Status}");
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
                "Apply operation IDs are duplicated or overlap.");
        }

        if (!completed.SequenceEqual(
                confirmed,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "Apply completion set differs from the confirmed preflight set.");
        }

        if (result.CreatedCount != created.Length ||
            result.CreateFailedCount != failed.Length ||
            result.CreatedRuntimeHandles.Count != created.Length ||
            result.DrawingWriteCount != created.Length ||
            result.DrawingWritePerformed != (created.Length > 0))
        {
            throw new InvalidDataException(
                "Apply counts or drawing-write receipt are inconsistent.");
        }

        if (result.PostMissingCount != 0)
        {
            throw new InvalidDataException(
                "Apply result still reports missing labels.");
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
                "正在启动已验证的 Console。"));

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "The verified Console process could not be started.");
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
                "The verified Console returned no JSON result.");
        }

        progress?.Report(
            new WorkflowProgress(
                80,
                "已收到 Console 结果，正在解析。"));

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

        return $"Console exited with code {exitCode}: {detail}";
    }
}
