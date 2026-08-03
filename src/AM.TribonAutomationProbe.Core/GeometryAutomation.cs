namespace AM.TribonAutomationProbe.Core;

public interface IGeometryAutomationAdapter
{
    Task<GeometryDetectionResult> DetectAsync(GeometryDetectionRequest request, CancellationToken cancellationToken);
    Task<GeometryHighlightResult> HighlightAsync(GeometryHighlightRequest request, CancellationToken cancellationToken);
    Task<GeometryHighlightClearResult> ClearHighlightAsync(GeometryHighlightClearRequest request, CancellationToken cancellationToken);
    Task<GeometryLabelPreflightResult> PreflightLabelsAsync(GeometryLabelPreflightRequest request, CancellationToken cancellationToken);
    Task<GeometryLabelApplyMissingResult> ApplyMissingLabelsAsync(GeometryLabelApplyMissingRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// In-process geometry facade used by the mock adapter and legacy read APIs.
/// Real Tribon writes are executed by FileBridgeGeometryAutomationAdapter.
/// </summary>
public sealed class GeometryAutomationAdapter(ITribonAdapter inner) : IGeometryAutomationAdapter
{
    public async Task<GeometryDetectionResult> DetectAsync(
        GeometryDetectionRequest request,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var value = await inner.DetectGeometryObjectsAsync(
            new GeometryObjectDetectionRequest(RequestId: request.OperationId),
            cancellationToken);

        return new(
            "1.0",
            request.TaskType,
            request.OperationId,
            request.DrawingContext,
            started,
            DateTimeOffset.UtcNow,
            value.Status,
            false,
            value.Objects,
            value.Diagnostics,
            false);
    }

    public async Task<GeometryHighlightResult> HighlightAsync(
        GeometryHighlightRequest request,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var value = await inner.DetectGeometryObjectsAsync(
            new GeometryObjectDetectionRequest(RequestId: request.OperationId),
            cancellationToken);

        var categories = request.Categories ?? Array.Empty<GeometryObjectCategory>();
        var objects = value.Objects
            .Where(x => categories.Contains(x.Category))
            .ToArray();

        var handles = objects
            .SelectMany(x => x.GeometryHandles)
            .Distinct(StringComparer.Ordinal)
            .Count();

        return new(
            "1.0",
            request.TaskType,
            request.OperationId,
            "current_drawing_contours",
            started,
            DateTimeOffset.UtcNow,
            "succeeded",
            false,
            objects.Length,
            handles,
            handles,
            0,
            0,
            categories,
            false);
    }

    public Task<GeometryHighlightClearResult> ClearHighlightAsync(
        GeometryHighlightClearRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            new GeometryHighlightClearResult(
                "1.0",
                request.TaskType,
                request.OperationId,
                "current_drawing_contours",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                "succeeded",
                true,
                false,
                false));

    public async Task<GeometryLabelPreflightResult> PreflightLabelsAsync(
        GeometryLabelPreflightRequest request,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;

        var detection = await inner.DetectGeometryObjectsAsync(
            new GeometryObjectDetectionRequest(RequestId: request.OperationId),
            cancellationToken);

        var labels = await inner.InspectGeometryLabelsAsync(
            new GeometryLabelInspectionRequest(RequestId: request.OperationId),
            cancellationToken);

        var ids = GeometryObjectDisplayIdAssigner.Assign(detection.Objects);
        var matches = GeometryObjectLabelMatcher.MatchResult(
            detection.Objects,
            ids,
            labels.Labels);

        var items = matches.Matches
            .Select(x => new GeometryLabelPreflightItem(
                "label:" + x.Identity.DisplayId,
                x.Identity.RuntimeObjectId,
                x.Identity.DisplayId,
                x.Identity.DisplayId,
                x.SameText.Count,
                0,
                0,
                x.Status == GeometryLabelMatchStatus.Matched
                    ? "ALREADY_APPLIED"
                    : x.Status == GeometryLabelMatchStatus.Duplicate
                        ? "BLOCKED_DUPLICATE"
                        : x.Status == GeometryLabelMatchStatus.TextConflict
                            ? "BLOCKED_TEXT_CONFLICT"
                            : "READY_TO_CREATE",
                x.Label?.RuntimeHandle))
            .ToArray();

        var duplicateCount = items.Count(
            x => x.Decision == "BLOCKED_DUPLICATE");

        var conflictCount = items.Count(
            x => x.Decision == "BLOCKED_TEXT_CONFLICT");

        var result = new GeometryLabelPreflightResult(
            "1.0",
            request.TaskType,
            request.OperationId,
            "current_drawing_contours",
            started,
            DateTimeOffset.UtcNow,
            duplicateCount > 0 || conflictCount > 0
                ? "BLOCKED"
                : "SUCCESS",
            items.Count(x => x.Decision == "ALREADY_APPLIED"),
            items.Count(x => x.Decision == "READY_TO_CREATE"),
            duplicateCount,
            0,
            items,
            false,
            false,
            conflictCount);

        return GeometryLabelPlanBinding.Attach(result);
    }

    public async Task<GeometryLabelApplyMissingResult> ApplyMissingLabelsAsync(
        GeometryLabelApplyMissingRequest request,
        CancellationToken cancellationToken)
    {
        GeometryLabelPlanBinding.ValidateAuthorization(request);

        var preflight = await PreflightLabelsAsync(
            new GeometryLabelPreflightRequest(
                OperationId:
                    request.ConfirmedPreflightOperationId),
            cancellationToken);

        GeometryLabelPlanBinding.ValidateAgainstPreflight(
            request,
            preflight);

        var blocked = preflight.Status == "BLOCKED";
        var createdIds = preflight.Items
            .Where(x => x.Decision == "READY_TO_CREATE")
            .Select(x => x.OperationId)
            .ToArray();

        var status = blocked
            ? "BLOCKED"
            : preflight.PreMissingCount == 0
                ? "ALREADY_COMPLETE"
                : "SUCCESS";

        var createdCount = status == "SUCCESS"
            ? preflight.PreMissingCount
            : 0;

        var postValid = status is "SUCCESS" or "ALREADY_COMPLETE"
            ? preflight.PreAlreadyPresentCount + createdCount
            : preflight.PreAlreadyPresentCount;

        // This facade is used for deterministic mock/local behavior only.
        // The real Tribon path is FileBridgeGeometryAutomationAdapter.
        return new GeometryLabelApplyMissingResult(
            SchemaVersion: "1.0",
            TaskType: request.TaskType,
            OperationId: request.OperationId,
            DrawingContext: preflight.DrawingContext,
            StartedAt: preflight.StartedAt,
            CompletedAt: DateTimeOffset.UtcNow,
            Status: status,
            CreatedCount: createdCount,
            CreateFailedCount: 0,
            PostValidLabelCount: postValid,
            PostMissingCount: blocked ? preflight.PreMissingCount : 0,
            PostDuplicateCount: preflight.PreDuplicateTextCount,
            PostCreatedValidCount: createdCount,
            PostCreatedPropertyErrorCount: 0,
            PostExistingMatchErrorCount: 0,
            PostExistingPropertyDriftCount: 0,
            PostInspectionErrorCount: preflight.PreInspectionErrorCount,
            DrawingWritePerformed: createdCount > 0,
            DrawingWriteCount: createdCount,
            ManualRecoveryRequired: false,
            CreatedRuntimeHandles: Array.Empty<string>(),
            FailedOperationIds: Array.Empty<string>(),
            SavePerformed: false,
            PreAlreadyPresentCount: preflight.PreAlreadyPresentCount,
            PreMissingCount: preflight.PreMissingCount,
            PreDuplicateTextCount: preflight.PreDuplicateTextCount,
            PreInspectionErrorCount: preflight.PreInspectionErrorCount,
            CreatedOperationIds: createdIds,
            ExistingPropertyDrifts: Array.Empty<GeometryLabelPropertyDrift>());
    }
}
