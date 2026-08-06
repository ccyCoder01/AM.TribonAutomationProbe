using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using AM.TribonAutomationProbe.Core;
using AM.TribonAutomationProbe.Desktop.Models;

namespace AM.TribonAutomationProbe.Desktop.Services;

public sealed class ConsoleAssistantWorkflowClient : IAssistantWorkflowClient
{
    private static readonly IReadOnlyDictionary<AssistantIntent, ExpectedTask>
        ExpectedTasks = new Dictionary<AssistantIntent, ExpectedTask>
        {
            [AssistantIntent.DetectGeometry] = new(
                "geometry.detect",
                AssistantTaskRisk.ReadOnly,
                false),
            [AssistantIntent.HighlightLifting] = new(
                "geometry.highlight-lifting",
                AssistantTaskRisk.ReadOnly,
                false),
            [AssistantIntent.HighlightFlanges] = new(
                "geometry.highlight-flanges",
                AssistantTaskRisk.ReadOnly,
                false),
            [AssistantIntent.ClearHighlight] = new(
                "geometry.highlight-clear",
                AssistantTaskRisk.ReadOnly,
                false),
            [AssistantIntent.PreflightLabels] = new(
                "geometry.label-preflight",
                AssistantTaskRisk.ReadOnly,
                false),
            [AssistantIntent.ApplyMissingLabels] = new(
                "geometry.label-apply-missing",
                AssistantTaskRisk.DrawingWrite,
                true)
        };

    public async Task<AssistantInterpretationEnvelope> InterpretAsync(
        ConsoleWorkflowSettings settings,
        string userText,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(userText))
        {
            throw new ArgumentException(
                "Natural-language input is required.",
                nameof(userText));
        }

        settings.Validate();

        var result = await RunConsoleAsync(
                settings,
                BuildInterpretArguments(settings, userText.Trim()),
                cancellationToken)
            .ConfigureAwait(false);

        ValidateInterpretation(result, userText.Trim());
        return result;
    }

    public static IReadOnlyList<string> BuildInterpretArguments(
        ConsoleWorkflowSettings settings,
        string userText)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(userText))
        {
            throw new ArgumentException(
                "Natural-language input is required.",
                nameof(userText));
        }

        return new[]
        {
            "assistant-interpret",
            "--adapter=mock",
            $"--bridge-root={Path.GetFullPath(settings.BridgeRoot)}",
            $"--timeout-ms={settings.TimeoutMs}",
            $"--poll-interval-ms={settings.PollIntervalMs}",
            $"--text={userText.Trim()}"
        };
    }

    public static void ValidateInterpretation(
        AssistantInterpretationEnvelope result,
        string expectedUserText)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!string.Equals(result.SchemaVersion, "1.0", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Assistant interpretation schemaVersion is invalid.");
        }

        if (!string.Equals(
                result.ProductName,
                AssistantTaskOrchestrator.ProductName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Assistant interpretation productName is invalid.");
        }

        if (result.ExecutionPerformed ||
            result.DrawingWritePerformed ||
            result.SavePerformed)
        {
            throw new InvalidDataException(
                "assistant-interpret unexpectedly reported execution, drawing write, or save activity.");
        }

        var interpretation = result.Interpretation ??
            throw new InvalidDataException(
                "Assistant interpretation payload is missing.");
        var plan = result.Plan ??
            throw new InvalidDataException(
                "Assistant task plan is missing.");

        if (!string.Equals(plan.SchemaVersion, "1.0", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(plan.PlanId) ||
            !string.Equals(plan.UserText, expectedUserText, StringComparison.Ordinal) ||
            plan.AutoSave)
        {
            throw new InvalidDataException(
                "Assistant plan identity or safety fields are invalid.");
        }

        if (plan.Tasks.Count > ExpectedTasks.Count)
        {
            throw new InvalidDataException(
                "Assistant plan contains too many tasks.");
        }

        if (interpretation.ClarificationRequired)
        {
            if (plan.State != AssistantTaskState.AwaitingClarification ||
                plan.Tasks.Count != 0 ||
                plan.ContainsWrite ||
                plan.RequiresConfirmation)
            {
                throw new InvalidDataException(
                    "Clarification response contains an executable task plan.");
            }

            return;
        }

        if (plan.Tasks.Count == 0 ||
            interpretation.Tasks.Count != plan.Tasks.Count)
        {
            throw new InvalidDataException(
                "Assistant plan does not match interpreted tasks.");
        }

        var expectedContainsWrite = false;
        var expectedRequiresConfirmation = false;

        for (var index = 0; index < plan.Tasks.Count; index++)
        {
            var planned = plan.Tasks[index];
            var interpreted = interpretation.Tasks[index];

            if (!ExpectedTasks.TryGetValue(
                    planned.Intent,
                    out var expected))
            {
                throw new InvalidDataException(
                    "Assistant plan contains an unregistered task.");
            }

            if (planned.Sequence != index + 1 ||
                planned.Intent != interpreted.Intent ||
                !string.Equals(
                    planned.TaskType,
                    expected.TaskType,
                    StringComparison.Ordinal) ||
                planned.Risk != expected.Risk ||
                planned.RequiresConfirmation != expected.RequiresConfirmation ||
                planned.AutoSave)
            {
                throw new InvalidDataException(
                    "Assistant plan contains an inconsistent task.");
            }

            expectedContainsWrite |=
                expected.Risk == AssistantTaskRisk.DrawingWrite;
            expectedRequiresConfirmation |=
                expected.RequiresConfirmation;
        }

        if (plan.ContainsWrite != expectedContainsWrite ||
            plan.RequiresConfirmation != expectedRequiresConfirmation ||
            plan.State != (expectedRequiresConfirmation
                ? AssistantTaskState.AwaitingConfirmation
                : AssistantTaskState.Planned))
        {
            throw new InvalidDataException(
                "Assistant plan risk, confirmation, or state fields are inconsistent.");
        }
    }

    private static async Task<AssistantInterpretationEnvelope> RunConsoleAsync(
        ConsoleWorkflowSettings settings,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var fullConsolePath = Path.GetFullPath(settings.ConsolePath);
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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        timeout.CancelAfter(settings.TimeoutMs);

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "The verified Console process could not be started.");
        }

        using var cancellationRegistration = timeout.Token.Register(
            () =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
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

        try
        {
            await process.WaitForExitAsync(timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Assistant interpretation exceeded {settings.TimeoutMs} milliseconds.");
        }

        var standardOutput = await standardOutputTask.ConfigureAwait(false);
        var standardError = await standardErrorTask.ConfigureAwait(false);

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
                "The verified Console returned no assistant interpretation JSON.");
        }

        try
        {
            return JsonSerializer.Deserialize<AssistantInterpretationEnvelope>(
                       standardOutput.Trim(),
                       JsonDefaults.Options) ??
                   throw new InvalidDataException(
                       "The assistant interpretation JSON result is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "The verified Console assistant output is not valid JSON.",
                ex);
        }
    }

    private sealed record ExpectedTask(
        string TaskType,
        AssistantTaskRisk Risk,
        bool RequiresConfirmation);
}
