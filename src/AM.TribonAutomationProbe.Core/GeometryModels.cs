namespace AM.TribonAutomationProbe.Core;

public enum GeometryObjectCategory { LIFTING_BEAM, LIFTING_LUG, PIPE_FLANGE_FRONT, PIPE_FLANGE_SIDE, STRUCTURAL_FLANGE }
public sealed record GeometryObjectDetectionRequest(string SchemaVersion = "1.0", string Action = "geometry_objects.detect", string RequestId = "", bool IncludeGeometryHandles = true);
public sealed record GeometryFeatureSummary(int GeometryCount = 0);
public sealed record GeometryObjectDetectionDiagnostics(int CapturedContourCount = 0, int AssignedUniqueContourCount = 0, int UnassignedContourCount = 0, int ConflictHandleCount = 0, int ParseFailureCount = 0);
public sealed record GeometryLabelInspectionDiagnostics(int DuplicateTextCount = 0, int ExtentReadFailureCount = 0, int TextPropertyFailureCount = 0);
public sealed record GeometryObjectApplyDiagnostics(bool WriteAuthorized, bool WriteAttempted, bool NetDrawingChange, bool RollbackAttempted, bool? RollbackSucceeded, bool SavePerformed = false);
public sealed record DetectedGeometryObject(string RuntimeObjectId, GeometryObjectCategory Category, string Confidence, LayoutRectangle Extent, IReadOnlyList<string> SeedHandles, IReadOnlyList<string> GeometryHandles, int GeometryCount, GeometryFeatureSummary Features);
public sealed record GeometryObjectDetectionResponse(string SchemaVersion, string RequestId, string Status, string Scope, LayoutRectangle DrawingExtent, IReadOnlyList<DetectedGeometryObject> Objects, GeometryObjectDetectionDiagnostics Diagnostics);
public sealed record GeometryLabelInspectionRequest(string SchemaVersion = "1.0", string Action = "geometry_labels.inspect", string RequestId = "");
public sealed record ExistingGeometryLabel(string RuntimeHandle, string Text, LayoutRectangle Extent);
public sealed record GeometryLabelInspectionResponse(string SchemaVersion, string RequestId, string Status, IReadOnlyList<ExistingGeometryLabel> Labels, GeometryLabelInspectionDiagnostics Diagnostics);
public sealed record GeometryObjectLabelMove(string OperationId, string RuntimeHandle, string ExpectedText, LayoutRectangle ExpectedExtent, LayoutRectangle DesiredExtent, double Dx, double Dy);
public sealed record GeometryObjectLabelApplyRequest(string SchemaVersion = "1.0", string Action = "geometry_labels.apply_moves", string RequestId = "", bool AllowWrite = false, IReadOnlyList<GeometryObjectLabelMove>? Moves = null);
public sealed record GeometryObjectLabelApplyReceipt(string OperationId, string RuntimeHandle, string Status, string? Error = null);
public sealed record GeometryObjectLabelApplyResponse(string SchemaVersion, string RequestId, string Status, bool SavePerformed, IReadOnlyList<GeometryObjectLabelApplyReceipt> Receipts, GeometryObjectApplyDiagnostics Diagnostics);

public sealed record GeometryDetectionRequest(string SchemaVersion = "1.0", string TaskType = "geometry.detect", string OperationId = "", string DrawingContext = "current_drafting_context");
public sealed record GeometryDetectionResult(string SchemaVersion, string TaskType, string OperationId, string DrawingContext, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, string Status, bool DrawingWritePerformed, IReadOnlyList<DetectedGeometryObject> Objects, GeometryObjectDetectionDiagnostics Diagnostics, bool SavePerformed = false);
public sealed record GeometryHighlightRequest(string SchemaVersion = "1.0", string TaskType = "geometry.highlight", string OperationId = "", IReadOnlyList<GeometryObjectCategory>? Categories = null);
public sealed record GeometryHighlightResult(string SchemaVersion, string TaskType, string OperationId, string DrawingContext, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, string Status, bool DrawingWritePerformed, int HighlightedObjectCount, int HighlightedHandleCount, int HighlightSuccessCount, int MissingHandleCount, int HighlightFailureCount, IReadOnlyList<GeometryObjectCategory> Categories, bool SavePerformed = false);
public sealed record GeometryHighlightClearRequest(string SchemaVersion = "1.0", string TaskType = "geometry.highlight-clear", string OperationId = "");
public sealed record GeometryHighlightClearResult(string SchemaVersion, string TaskType, string OperationId, string DrawingContext, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, string Status, bool Cleared, bool DrawingWritePerformed, bool SavePerformed = false);
public sealed record GeometryLabelPreflightRequest(string SchemaVersion = "1.0", string TaskType = "geometry.label-preflight", string OperationId = "");
public sealed record GeometryLabelPreflightResult(string SchemaVersion, string TaskType, string OperationId, string DrawingContext, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, string Status, int PreAlreadyPresentCount, int PreMissingCount, int PreDuplicateTextCount, int PreInspectionErrorCount, IReadOnlyList<GeometryLabelPreflightItem> Items, bool DrawingWritePerformed, bool SavePerformed = false, int PreTextConflictCount = 0, string PlanHash = "", IReadOnlyList<string>? ReadyOperationIds = null);
public sealed record GeometryLabelPreflightItem(string OperationId, string SourceObjectId, string StableObjectId, string ExpectedText, int MatchCount, double NearestDistance, double AllowedDistance, string Decision, string? MatchHandle = null);
public sealed record GeometryLabelApplyMissingRequest(
    string SchemaVersion = "1.0",
    string TaskType = "geometry.label-apply-missing",
    string OperationId = "",
    bool AllowWrite = false,
    bool WriteConfirmed = false,
    string ConfirmedPreflightOperationId = "",
    string ConfirmedPlanHash = "",
    IReadOnlyList<string>? ConfirmedOperationIds = null);
public sealed record GeometryLabelPropertyDrift(
    string OperationId,
    string StableObjectId,
    IReadOnlyList<string> Fields,
    double ActualX,
    double ActualY,
    double ActualHeight,
    string ActualColour,
    double PlannedX,
    double PlannedY,
    double PlannedHeight,
    string PlannedColour);

public sealed record GeometryLabelApplyMissingResult(
    string SchemaVersion,
    string TaskType,
    string OperationId,
    string DrawingContext,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string Status,
    int CreatedCount,
    int CreateFailedCount,
    int PostValidLabelCount,
    int PostMissingCount,
    int PostDuplicateCount,
    int PostCreatedValidCount,
    int PostCreatedPropertyErrorCount,
    int PostExistingMatchErrorCount,
    int PostExistingPropertyDriftCount,
    int PostInspectionErrorCount,
    bool DrawingWritePerformed,
    int DrawingWriteCount,
    bool ManualRecoveryRequired,
    IReadOnlyList<string> CreatedRuntimeHandles,
    IReadOnlyList<string> FailedOperationIds,
    bool SavePerformed = false,
    int PreAlreadyPresentCount = 0,
    int PreMissingCount = 0,
    int PreDuplicateTextCount = 0,
    int PreInspectionErrorCount = 0,
    IReadOnlyList<string>? CreatedOperationIds = null,
    IReadOnlyList<GeometryLabelPropertyDrift>? ExistingPropertyDrifts = null);
