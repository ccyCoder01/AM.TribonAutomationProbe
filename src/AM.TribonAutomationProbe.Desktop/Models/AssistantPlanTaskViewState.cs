using AM.TribonAutomationProbe.Core;

namespace AM.TribonAutomationProbe.Desktop.Models;

public sealed record AssistantPlanTaskViewState(
    int Sequence,
    AssistantIntent Intent,
    string TaskType,
    string DisplayName,
    AssistantTaskRisk Risk,
    string RiskText,
    bool RequiresConfirmation,
    string ConfirmationText);
