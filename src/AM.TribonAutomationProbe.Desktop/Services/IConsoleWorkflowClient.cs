using AM.TribonAutomationProbe.Core;
using AM.TribonAutomationProbe.Desktop.Models;

namespace AM.TribonAutomationProbe.Desktop.Services;

public interface IConsoleWorkflowClient
{
    Task<GeometryLabelPreflightResult> RunPreflightAsync(
        ConsoleWorkflowSettings settings,
        IProgress<WorkflowProgress>? progress,
        CancellationToken cancellationToken);

    Task<GeometryLabelApplyMissingResult> RunApplyAsync(
        ConsoleWorkflowSettings settings,
        GeometryLabelPreflightResult confirmedPreflight,
        IProgress<WorkflowProgress>? progress,
        CancellationToken cancellationToken);
}
