namespace AM.TribonAutomationProbe.Core;

public enum AssistantIntent
{
    Unsupported,
    Ambiguous,
    DetectGeometry,
    HighlightLifting,
    HighlightFlanges,
    ClearHighlight,
    PreflightLabels,
    ApplyMissingLabels
}

public enum AssistantTaskState
{
    Received,
    Interpreting,
    Planned,
    AwaitingClarification,
    AwaitingConfirmation,
    Queued,
    WaitingForTribon,
    Executing,
    Verifying,
    Completed,
    Failed,
    Cancelled
}

public enum AssistantTaskRisk
{
    ReadOnly,
    DrawingWrite
}

public sealed record AssistantConversationTurn(string Role, string Content);

public sealed record AssistantConversationContext(
    string UserText,
    IReadOnlyList<AssistantConversationTurn>? History = null);

public sealed record AssistantInterpretedTask(
    AssistantIntent Intent,
    double Confidence,
    IReadOnlyDictionary<string, string>? Arguments = null);

public sealed record AssistantInterpretation(
    string Provider,
    string Model,
    IReadOnlyList<AssistantInterpretedTask> Tasks,
    bool ClarificationRequired,
    string? ClarificationQuestion = null,
    string? Explanation = null,
    string? RequestId = null,
    string? ResponseId = null,
    long? LatencyMs = null,
    bool FallbackUsed = false,
    string? FallbackReason = null);

public sealed record AssistantModelExecutionInfo(
    string Provider,
    string Model,
    string? RequestId = null,
    string? ResponseId = null,
    long? LatencyMs = null,
    bool FallbackUsed = false,
    string? FallbackReason = null);

public interface IAssistantLanguageModel
{
    Task<AssistantInterpretation> InterpretAsync(
        AssistantConversationContext context,
        CancellationToken cancellationToken);
}

public sealed record AssistantPlannedTask(
    int Sequence,
    AssistantIntent Intent,
    string TaskType,
    AssistantTaskRisk Risk,
    bool RequiresConfirmation,
    bool AutoSave,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record AssistantTaskPlan(
    string SchemaVersion,
    string PlanId,
    string UserText,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AssistantPlannedTask> Tasks,
    bool RequiresConfirmation,
    bool ContainsWrite,
    bool AutoSave,
    AssistantTaskState State,
    string Message);

public sealed record AssistantExecutionAuthorization(
    bool AllowWrite = false,
    bool WriteConfirmed = false);

public sealed record AssistantProgressUpdate(
    int Sequence,
    AssistantTaskState State,
    string Message,
    DateTimeOffset Timestamp,
    string? TaskType = null);

public sealed record AssistantExecutionError(
    string Code,
    string Category,
    string Message,
    bool Retryable = false);

public sealed record AssistantTaskExecutionResult(
    int Sequence,
    string TaskType,
    AssistantTaskState State,
    string Status,
    bool DrawingWritePerformed,
    bool SavePerformed,
    string Summary,
    object? RawResult = null,
    AssistantExecutionError? Error = null);

public sealed record AssistantRunResult(
    string SchemaVersion,
    string ProductName,
    string RunId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    AssistantTaskState State,
    AssistantTaskPlan? Plan,
    IReadOnlyList<AssistantProgressUpdate> Progress,
    IReadOnlyList<AssistantTaskExecutionResult> TaskResults,
    string Summary,
    AssistantExecutionError? Error = null,
    AssistantModelExecutionInfo? Model = null);
