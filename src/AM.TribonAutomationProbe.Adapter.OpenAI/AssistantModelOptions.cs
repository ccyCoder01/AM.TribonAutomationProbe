namespace AM.TribonAutomationProbe.Adapter.OpenAI;

public sealed class AssistantModelOptions
{
    public AssistantModelOptions(string baseUrl, string apiKey, string model)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("BaseUrl, ApiKey and Model are required.");
        var uri = new Uri(baseUrl.Trim().TrimEnd('/'), UriKind.Absolute);
        if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.Query))
            throw new ArgumentException("BaseUrl must be an absolute HTTPS URL without credentials, query, or fragment.");
        BaseUrl = uri.ToString().TrimEnd('/'); ApiKey = apiKey.Trim(); Model = model.Trim();
        ChatCompletionsEndpoint = new Uri(BaseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) ? BaseUrl : BaseUrl + "/chat/completions", UriKind.Absolute);
    }
    public string BaseUrl { get; }
    public string ApiKey { get; }
    public string Model { get; }
    public Uri ChatCompletionsEndpoint { get; }
    public override string ToString() => $"BaseUrl={BaseUrl}; Model={Model}; ApiKey=***";
}
