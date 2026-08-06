using System.Security;
using AM.TribonAutomationProbe.Desktop.Models;

namespace AM.TribonAutomationProbe.Desktop.Services;

public interface IAssistantWorkflowClient
{
    Task<AssistantInterpretationEnvelope> InterpretAsync(
        ConsoleWorkflowSettings settings,
        AssistantProviderSessionSettings providerSettings,
        SecureString? authorizationSecret,
        string userText,
        CancellationToken cancellationToken);
}
