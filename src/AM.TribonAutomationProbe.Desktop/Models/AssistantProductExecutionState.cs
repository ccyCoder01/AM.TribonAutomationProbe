namespace AM.TribonAutomationProbe.Desktop.Models;

public enum AssistantProductExecutionState
{
    Idle,
    Planning,
    Validating,
    Executing,
    AwaitingWriteConfirmation,
    ExecutingWrite,
    Completed,
    Failed,
    Cancelled,
    RuntimeUnavailable
}
