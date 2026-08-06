namespace AM.TribonAutomationProbe.Desktop.Models;

public sealed record AssistantConversationMessage(
    string Role,
    string Content,
    DateTimeOffset Timestamp)
{
    public string DisplayRole => Role switch
    {
        "user" => "用户",
        "assistant" => "助手",
        _ => "系统"
    };
}
