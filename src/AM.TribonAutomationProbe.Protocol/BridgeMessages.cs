using AM.TribonAutomationProbe.Core;

namespace AM.TribonAutomationProbe.Protocol;

public static class BridgeConstants
{
    public const string Protocol = "AM.TribonBridge";
    public const string Version = "0.1";
    public static readonly IReadOnlySet<string> Actions = new HashSet<string>(StringComparer.Ordinal) { "context.get", "annotation.export", "annotation.move", "geometry_objects.detect", "geometry_labels.inspect", "geometry_labels.apply_moves", "geometry.detect", "geometry.highlight-lifting", "geometry.highlight-flanges", "geometry.highlight-clear", "geometry.label-preflight", "geometry.label-apply-missing" };
}

public sealed record BridgeExecutionOptions(int TimeoutMs = 30000);
public sealed record BridgeCommand
{
    public string Protocol { get; init; } = BridgeConstants.Protocol;
    public string Version { get; init; } = BridgeConstants.Version;
    public string MessageType { get; init; } = "bridge.command";
    public required string MessageId { get; init; }
    public required string CommandId { get; init; }
    public required string CorrelationId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public required string Action { get; init; }
    public object Payload { get; init; } = new { };
    public BridgeExecutionOptions Execution { get; init; } = new();
}

public sealed record BridgeError(string Code, string Category, string Message, bool Retryable = false, object? Details = null);
public sealed record BridgeResult
{
    public string Protocol { get; init; } = BridgeConstants.Protocol;
    public string Version { get; init; } = BridgeConstants.Version;
    public string MessageType { get; init; } = "bridge.result";
    public required string MessageId { get; init; }
    public required string CommandId { get; init; }
    public required string CorrelationId { get; init; }
    public required string CausationId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public required string Status { get; init; }
    public object? Result { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public BridgeError? Error { get; init; }
}

public static class BridgeMessageValidator
{
    public static void ValidateCommand(BridgeCommand command)
    {
        if (command.Protocol != BridgeConstants.Protocol || command.Version != BridgeConstants.Version || command.MessageType != "bridge.command")
            throw new ProbeException(ProbeErrorCodes.InvalidMessage, "Invalid bridge command envelope", "validation");
        if (!BridgeConstants.Actions.Contains(command.Action))
            throw new ProbeException(ProbeErrorCodes.UnsupportedAction, $"Unsupported action: {command.Action}", "validation");
        if (command.Execution.TimeoutMs <= 0)
            throw new ProbeException(ProbeErrorCodes.InvalidMessage, "timeoutMs must be greater than zero", "validation");
    }

    public static void ValidateResult(BridgeResult result)
    {
        if (result.Protocol != BridgeConstants.Protocol || result.Version != BridgeConstants.Version || result.MessageType != "bridge.result")
            throw new ProbeException(ProbeErrorCodes.InvalidResultMessage, "Invalid bridge result envelope", "validation");
        if (result.Status is not ("succeeded" or "failed"))
            throw new ProbeException(ProbeErrorCodes.InvalidResultMessage, "Result status must be succeeded or failed", "validation");
    }
}


