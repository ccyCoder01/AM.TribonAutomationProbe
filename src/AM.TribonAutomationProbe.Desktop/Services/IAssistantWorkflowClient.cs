using AM.TribonAutomationProbe.Desktop.Models;

namespace AM.TribonAutomationProbe.Desktop.Services;

public interface IAssistantWorkflowClient
{
    Task<AssistantInterpretationEnvelope> InterpretAsync(
        ConsoleWorkflowSettings settings,
        string userText,
        CancellationToken cancellationToken);
}
