using AM.TribonAutomationProbe.Core;
using Xunit;

namespace AM.TribonAutomationProbe.Tests;

public sealed class FallbackAssistantLanguageModelTests
{
    [Fact]
    public async Task RetryablePrimaryFailureUsesRuleFallback()
    {
        var primary = new ThrowingLanguageModel(
            new ProbeException(
                ProbeErrorCodes.AssistantModelRateLimited,
                "rate limited",
                "model_rate_limit",
                true));
        var model = new FallbackAssistantLanguageModel(
            primary,
            new RuleBasedAssistantLanguageModel());

        var result = await model.InterpretAsync(
            new AssistantConversationContext("高亮所有法兰"),
            CancellationToken.None);

        Assert.True(result.FallbackUsed);
        Assert.Equal(
            ProbeErrorCodes.AssistantModelRateLimited,
            result.FallbackReason);
        Assert.Equal("deterministic", result.Provider);
        Assert.Equal(AssistantIntent.HighlightFlanges, Assert.Single(result.Tasks).Intent);
    }

    [Fact]
    public async Task NonRetryablePrimaryFailureIsNotHiddenByFallback()
    {
        var expected = new ProbeException(
            ProbeErrorCodes.AssistantModelAuthentication,
            "authentication failed",
            "model_authentication");
        var model = new FallbackAssistantLanguageModel(
            new ThrowingLanguageModel(expected),
            new RuleBasedAssistantLanguageModel());

        var actual = await Assert.ThrowsAsync<ProbeException>(
            () => model.InterpretAsync(
                new AssistantConversationContext("高亮所有法兰"),
                CancellationToken.None));

        Assert.Same(expected, actual);
    }

    private sealed class ThrowingLanguageModel(ProbeException exception)
        : IAssistantLanguageModel
    {
        public Task<AssistantInterpretation> InterpretAsync(
            AssistantConversationContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw exception;
        }
    }
}
