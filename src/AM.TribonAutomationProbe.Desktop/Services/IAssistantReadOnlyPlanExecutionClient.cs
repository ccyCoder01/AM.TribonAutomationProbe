using AM.TribonAutomationProbe.Core;
using AM.TribonAutomationProbe.Desktop.Models;

namespace AM.TribonAutomationProbe.Desktop.Services;

public interface IAssistantReadOnlyPlanExecutionClient
{
    Task<AssistantTaskExecutionResult> ExecuteAsync(
        ConsoleWorkflowSettings settings,
        AssistantTaskPlan plan,
        IProgress<WorkflowProgress>? progress,
        CancellationToken cancellationToken);
}
