namespace AM.TribonAutomationProbe.Desktop.Models;

public enum AssistantProviderMode
{
    RuleBased,
    OpenAiCompatible
}

public enum AssistantAuthorizationMode
{
    BearerToken,
    RawAuthorizationValue
}

public sealed record AssistantProviderSessionSettings(
    AssistantProviderMode Mode,
    string BaseUrl,
    string Model,
    AssistantAuthorizationMode AuthorizationMode)
{
    public static AssistantProviderSessionSettings RuleBased { get; } =
        new(
            AssistantProviderMode.RuleBased,
            string.Empty,
            string.Empty,
            AssistantAuthorizationMode.BearerToken);

    public bool RequiresAuthorizationSecret =>
        Mode == AssistantProviderMode.OpenAiCompatible;

    public string DisplayName =>
        Mode == AssistantProviderMode.OpenAiCompatible
            ? $"{Model.Trim()} @ {GetHostDisplay()}"
            : "内置规则模型";

    public void Validate()
    {
        if (Mode == AssistantProviderMode.RuleBased)
        {
            return;
        }

        if (Mode != AssistantProviderMode.OpenAiCompatible)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Mode),
                "Unsupported assistant provider mode.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw new ArgumentException(
                "Assistant Base URL is required.",
                nameof(BaseUrl));
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            throw new ArgumentException(
                "Assistant model ID is required.",
                nameof(Model));
        }

        if (!Uri.TryCreate(
                BaseUrl.Trim(),
                UriKind.Absolute,
                out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "Assistant Base URL must be absolute HTTPS without credentials, query, or fragment.",
                nameof(BaseUrl));
        }
    }

    public string NormalizedBaseUrl()
    {
        Validate();
        return BaseUrl.Trim().TrimEnd('/');
    }

    public string NormalizedModel()
    {
        Validate();
        return Model.Trim();
    }

    private string GetHostDisplay()
    {
        if (Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }

        return "-";
    }
}
