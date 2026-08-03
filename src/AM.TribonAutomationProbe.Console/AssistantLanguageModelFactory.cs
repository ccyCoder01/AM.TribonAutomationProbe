using AM.TribonAutomationProbe.Adapter.OpenAI;
using AM.TribonAutomationProbe.Core;

public static class AssistantLanguageModelFactory
{
    public static IAssistantLanguageModel Create(
        CliOptions options,
        Func<string, string?>? environment = null,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        environment ??= Environment.GetEnvironmentVariable;

        var baseUrl = options.AssistantBaseUrl ?? environment("ASSISTANT_BASE_URL");
        var apiKey = environment("ASSISTANT_API_KEY");
        var model = options.AssistantModel ?? environment("ASSISTANT_MODEL");
        if (string.IsNullOrWhiteSpace(baseUrl) && string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(model)) return new RuleBasedAssistantLanguageModel();
        var missing = new[] { ("base_url", baseUrl), ("api_key", apiKey), ("model", model) }.Where(x => string.IsNullOrWhiteSpace(x.Item2)).Select(x => x.Item1).ToArray();
        if (missing.Length > 0) throw new ProbeException(ProbeErrorCodes.AssistantModelConfiguration, "Missing assistant model fields: " + string.Join(", ", missing), "model_configuration");
        return new OpenAiCompatibleChatCompletionsAssistantLanguageModel(httpClient ?? new HttpClient(), new AssistantModelOptions(baseUrl!, apiKey!, model!));
    }
}
