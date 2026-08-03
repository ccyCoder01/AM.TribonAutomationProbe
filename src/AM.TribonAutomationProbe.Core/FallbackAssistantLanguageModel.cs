namespace AM.TribonAutomationProbe.Core;

/// <summary>
/// Uses a deterministic fallback only for retryable primary-provider failures.
/// Authentication, configuration, schema, and refusal failures remain visible.
/// </summary>
public sealed class FallbackAssistantLanguageModel(
    IAssistantLanguageModel primary,
    IAssistantLanguageModel fallback) : IAssistantLanguageModel
{
    public async Task<AssistantInterpretation> InterpretAsync(
        AssistantConversationContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return await primary.InterpretAsync(context, cancellationToken);
        }
        catch (ProbeException ex) when (ex.Retryable)
        {
            var result = await fallback.InterpretAsync(
                context,
                cancellationToken);

            return result with
            {
                FallbackUsed = true,
                FallbackReason = ex.Code
            };
        }
    }
}
