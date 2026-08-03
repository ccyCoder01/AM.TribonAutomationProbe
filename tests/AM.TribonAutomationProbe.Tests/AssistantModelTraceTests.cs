using AM.TribonAutomationProbe.Adapter.Mock;
using AM.TribonAutomationProbe.Core;
using Xunit;

namespace AM.TribonAutomationProbe.Tests;

public sealed class AssistantModelTraceTests
{
    [Fact]
    public async Task OrchestratorSurfacesProviderTraceWithoutSecrets()
    {
        var model = new StaticLanguageModel(
            new AssistantInterpretation(
                Provider: "openai",
                Model: "test-model",
                Tasks:
                [
                    new AssistantInterpretedTask(
                        AssistantIntent.DetectGeometry,
                        0.99)
                ],
                ClarificationRequired: false,
                RequestId: "req_123",
                ResponseId: "resp_123",
                LatencyMs: 42));
        var mock = new MockTribonAdapter();
        var orchestrator = new AssistantTaskOrchestrator(
            model,
            new AssistantTaskPlanner(),
            new GeometryAutomationAdapter(mock),
            new AssistantResultFormatter());

        var result = await orchestrator.RunAsync(
            new AssistantConversationContext("识别对象"),
            new AssistantExecutionAuthorization(),
            cancellationToken: CancellationToken.None);

        Assert.Equal(AssistantTaskState.Completed, result.State);
        Assert.NotNull(result.Model);
        Assert.Equal("openai", result.Model!.Provider);
        Assert.Equal("test-model", result.Model.Model);
        Assert.Equal("req_123", result.Model.RequestId);
        Assert.Equal("resp_123", result.Model.ResponseId);
        Assert.Equal(42L, result.Model.LatencyMs!.Value);
        Assert.False(result.Model.FallbackUsed);
    }

    private sealed class StaticLanguageModel(AssistantInterpretation result)
        : IAssistantLanguageModel
    {
        public Task<AssistantInterpretation> InterpretAsync(
            AssistantConversationContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }
}
