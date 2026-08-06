using AM.TribonAutomationProbe.Core;

namespace AM.TribonAutomationProbe.Desktop.Models;

public sealed record AssistantInterpretationEnvelope(
    string SchemaVersion,
    string ProductName,
    AssistantInterpretation Interpretation,
    AssistantTaskPlan Plan,
    bool ExecutionPerformed,
    bool DrawingWritePerformed,
    bool SavePerformed);
