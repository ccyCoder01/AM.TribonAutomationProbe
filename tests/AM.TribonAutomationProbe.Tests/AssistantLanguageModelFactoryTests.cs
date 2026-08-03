using AM.TribonAutomationProbe.Adapter.OpenAI;
using AM.TribonAutomationProbe.Core;
using Xunit;

namespace AM.TribonAutomationProbe.Tests;

public sealed class AssistantLanguageModelFactoryTests
{
    [Fact] public void AllMissingUsesRuleBased() => Assert.IsType<RuleBasedAssistantLanguageModel>(AssistantLanguageModelFactory.Create(CliParser.Parse(["assistant"]).Options!, _ => null));
    [Theory] [InlineData("https://x", null, null)] [InlineData(null, "key", null)] [InlineData(null, null, "model")] public void PartialConfigurationIsRejected(string? url, string? key, string? model)
    { var ex = Assert.Throws<ProbeException>(() => AssistantLanguageModelFactory.Create(CliParser.Parse(["assistant", url is null ? "--text=x" : "--base-url=" + url, model is null ? "--text=x" : "--model=" + model]).Options!, n => n switch { "ASSISTANT_BASE_URL" => url, "ASSISTANT_API_KEY" => key, "ASSISTANT_MODEL" => model, _ => null })); Assert.Equal(ProbeErrorCodes.AssistantModelConfiguration, ex.Code); if (!string.IsNullOrEmpty(key)) Assert.DoesNotContain(key, ex.Message, StringComparison.Ordinal); }
    [Fact] public void CompleteEnvironmentConfigurationCreatesCompatibleProvider()
    { var model = AssistantLanguageModelFactory.Create(CliParser.Parse(["assistant"]).Options!, n => n switch { "ASSISTANT_BASE_URL" => "https://example.invalid/v1", "ASSISTANT_API_KEY" => "secret", "ASSISTANT_MODEL" => "m", _ => null }); Assert.IsType<OpenAiCompatibleChatCompletionsAssistantLanguageModel>(model); }
    [Fact] public void CliOverridesUrlAndModel() { var options = CliParser.Parse(["assistant", "--base-url=https://example.invalid/v1", "--model=cli"]).Options!; var model = AssistantLanguageModelFactory.Create(options, n => n == "ASSISTANT_API_KEY" ? "secret" : n == "ASSISTANT_BASE_URL" ? "https://env.invalid" : "env"); Assert.IsType<OpenAiCompatibleChatCompletionsAssistantLanguageModel>(model); }
}
