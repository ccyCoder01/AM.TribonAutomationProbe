namespace AM.TribonAutomationProbe.Core;

public sealed record Point2D(double X, double Y)
{
    public bool IsWithinTolerance(Point2D other, double tolerance) =>
        Math.Abs(X - other.X) <= tolerance && Math.Abs(Y - other.Y) <= tolerance;
}

public sealed record ObjectFallbackLocator(string? Name = null, int? Index = null);

public sealed record TribonObjectRef
{
    public required string ObjectType { get; init; }
    public string? PersistentId { get; init; }
    public ObjectFallbackLocator? FallbackLocator { get; init; }
    public string? Fingerprint { get; init; }
}

public sealed record TribonDrawingRef(string Id, string Name, bool Writable, string Revision);
public sealed record TribonViewRef(string Id, string Name);

public sealed record TribonContext
{
    public bool SessionActive { get; init; }
    public required string Module { get; init; }
    public required string DatabaseName { get; init; }
    public TribonDrawingRef? Drawing { get; init; }
    public TribonViewRef? View { get; init; }
}

public sealed record AnnotationSnapshot
{
    public required TribonObjectRef ObjectRef { get; init; }
    public required string ObjectType { get; init; }
    public string? Text { get; init; }
    public required Point2D Position { get; init; }
    public IReadOnlyList<Point2D> LeaderPoints { get; init; } = Array.Empty<Point2D>();
    public bool Locked { get; init; }
}

public sealed record AnnotationExportRequest
{
    public string Scope { get; init; } = "current_view";
    public IReadOnlyList<string> Types { get; init; } = ["label", "dimension", "general_text"];
    public string CoordinateSystem { get; init; } = "drawing";
    public string CoordinateUnit { get; init; } = "mm";
}

public sealed record AnnotationExportResult(TribonContext Context, IReadOnlyList<AnnotationSnapshot> Annotations);
public sealed record TribonContextResult(TribonContext Context);

public sealed record MoveAnnotationRequest
{
    public required TribonObjectRef ObjectRef { get; init; }
    public required Point2D ExpectedPosition { get; init; }
    public required Point2D DesiredPosition { get; init; }
    public string CoordinateSystem { get; init; } = "drawing";
    public string CoordinateUnit { get; init; } = "mm";
    public bool RefreshAfterMove { get; init; } = true;
    public bool SaveAfterMove { get; init; }
    public bool VerifyAfterMove { get; init; } = true;
    public double PositionToleranceMm { get; init; } = 0.01;
}

public sealed record AnnotationValidationRequest(TribonObjectRef ObjectRef, Point2D ExpectedPosition, double ToleranceMm);
public sealed record AnnotationValidationResult(bool Succeeded, Point2D? ActualPosition, string? ErrorCode = null);
public sealed record MoveAnnotationResult(TribonObjectRef ObjectRef, Point2D Before, Point2D Requested, Point2D? Actual, bool WriteSucceeded, bool RefreshSucceeded, bool VerificationSucceeded, bool Saved);

public static class ProbeErrorCodes
{
    public const string InvalidMessage = "INVALID_MESSAGE";
    public const string InvalidResultMessage = "INVALID_RESULT_MESSAGE";
    public const string UnsupportedProtocolVersion = "UNSUPPORTED_PROTOCOL_VERSION";
    public const string UnsupportedAction = "UNSUPPORTED_ACTION";
    public const string TribonNotRunning = "TRIBON_NOT_RUNNING";
    public const string WrongTribonModule = "WRONG_TRIBON_MODULE";
    public const string NoActiveDrawing = "NO_ACTIVE_DRAWING";
    public const string NoActiveView = "NO_ACTIVE_VIEW";
    public const string DrawingReadOnly = "DRAWING_READ_ONLY";
    public const string ObjectNotFound = "OBJECT_NOT_FOUND";
    public const string ObjectStateChanged = "OBJECT_STATE_CHANGED";
    public const string ExportFailed = "EXPORT_FAILED";
    public const string WriteFailed = "WRITE_FAILED";
    public const string RefreshFailed = "REFRESH_FAILED";
    public const string VerificationFailed = "VERIFICATION_FAILED";
    public const string SaveFailed = "SAVE_FAILED";
    public const string CommandTimeout = "COMMAND_TIMEOUT";
    public const string TransportFailed = "TRANSPORT_FAILED";
    public const string InternalError = "INTERNAL_ERROR";
    public const string AssistantModelConfiguration = "ASSISTANT_MODEL_CONFIGURATION";
    public const string AssistantModelAuthentication = "ASSISTANT_MODEL_AUTHENTICATION";
    public const string AssistantModelRateLimited = "ASSISTANT_MODEL_RATE_LIMITED";
    public const string AssistantModelTimeout = "ASSISTANT_MODEL_TIMEOUT";
    public const string AssistantModelUnavailable = "ASSISTANT_MODEL_UNAVAILABLE";
    public const string AssistantModelRequestRejected = "ASSISTANT_MODEL_REQUEST_REJECTED";
    public const string AssistantModelInvalidResponse = "ASSISTANT_MODEL_INVALID_RESPONSE";
    public const string AssistantModelRefusal = "ASSISTANT_MODEL_REFUSAL";
}

public sealed class ProbeException(string code, string message, string category = "execution", bool retryable = false, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
    public string Category { get; } = category;
    public bool Retryable { get; } = retryable;
}

public interface ITribonAdapter
{
    Task<TribonContextResult> GetContextAsync(CancellationToken cancellationToken);
    Task<AnnotationExportResult> ExportAnnotationsAsync(AnnotationExportRequest request, CancellationToken cancellationToken);
    Task<MoveAnnotationResult> MoveAnnotationAsync(MoveAnnotationRequest request, CancellationToken cancellationToken);
    Task<AnnotationValidationResult> ValidateAnnotationAsync(AnnotationValidationRequest request, CancellationToken cancellationToken);
    Task<GeometryObjectDetectionResponse> DetectGeometryObjectsAsync(GeometryObjectDetectionRequest request, CancellationToken cancellationToken);
    Task<GeometryLabelInspectionResponse> InspectGeometryLabelsAsync(GeometryLabelInspectionRequest request, CancellationToken cancellationToken);
    Task<GeometryObjectLabelApplyResponse> ApplyGeometryLabelMovesAsync(GeometryObjectLabelApplyRequest request, CancellationToken cancellationToken);
}
