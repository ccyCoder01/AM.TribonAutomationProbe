using System.Text.Json;
using AM.TribonAutomationProbe.Core;
using AM.TribonAutomationProbe.Protocol;

namespace AM.TribonAutomationProbe.Adapter.FileBridge;

public sealed record FileBridgeOptions(string RootDirectory = "./tribon-bridge", int PollIntervalMs = 200, int DefaultTimeoutMs = 30000);

public sealed class FileBridgeTransport(FileBridgeOptions options)
{
    private readonly JsonSerializerOptions _json = JsonDefaults.Options;
    public async Task<BridgeResult> SendAsync(BridgeCommand command, CancellationToken cancellationToken)
    {
        var effectiveTimeoutMs =
            command.Execution.TimeoutMs <= 0
                ? options.DefaultTimeoutMs
                : command.Execution.TimeoutMs;

        var effectiveCommand = command with
        {
            Execution = new BridgeExecutionOptions(effectiveTimeoutMs)
        };

        BridgeMessageValidator.ValidateCommand(effectiveCommand);
        var root = Path.GetFullPath(options.RootDirectory);
        var inbox = Path.Combine(root, "inbox");
        var output = Path.Combine(root, "output");
        foreach (var dir in new[] { inbox, Path.Combine(root, "processing"), output, Path.Combine(root, "failed"), Path.Combine(root, "archive"), Path.Combine(root, "logs") }) Directory.CreateDirectory(dir);
        var filename = $"{command.CreatedAt.UtcDateTime:yyyyMMdd'T'HHmmssfffffff'Z'}_{command.CommandId}.request.json";
        var target = Path.Combine(inbox, filename);
        var temp = target + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(effectiveCommand, _json), new System.Text.UTF8Encoding(false), cancellationToken);
        File.Move(temp, target, true);
        var resultPath = Path.Combine(output, command.CommandId + ".result.json");
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(effectiveTimeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(resultPath))
            {
                try
                {
                    var result = JsonSerializer.Deserialize<BridgeResult>(await File.ReadAllTextAsync(resultPath, cancellationToken), _json) ?? throw new ProbeException(ProbeErrorCodes.InvalidResultMessage, "Result JSON was empty", "validation");
                    BridgeMessageValidator.ValidateResult(result);
                    ValidateCorrelation(command, result);
                    return result;
                }
                catch (JsonException ex) { throw new ProbeException(ProbeErrorCodes.InvalidResultMessage, "Result JSON is invalid", "validation", false, ex); }
            }
            await Task.Delay(options.PollIntervalMs, cancellationToken);
        }
        throw new ProbeException(ProbeErrorCodes.CommandTimeout, $"Timed out waiting for {command.CommandId}", "timeout", true);
    }

    private static void ValidateCorrelation(BridgeCommand command, BridgeResult result)
    {
        if (!string.Equals(result.CommandId, command.CommandId, StringComparison.Ordinal))
            throw new ProbeException(ProbeErrorCodes.InvalidResultMessage, "Result commandId does not match request", "validation");

        if (!string.Equals(result.CorrelationId, command.CorrelationId, StringComparison.Ordinal))
            throw new ProbeException(ProbeErrorCodes.InvalidResultMessage, "Result correlationId does not match request", "validation");

        if (!string.Equals(result.CausationId, command.MessageId, StringComparison.Ordinal))
            throw new ProbeException(ProbeErrorCodes.InvalidResultMessage, "Result causationId does not match request messageId", "validation");
    }
}

public sealed class FileBridgeTribonAdapter(FileBridgeTransport transport) : ITribonAdapter
{
    private readonly FileBridgeTransport _transport = transport;
    public async Task<TribonContextResult> GetContextAsync(CancellationToken cancellationToken)
    {
        var result = await SendAsync("context.get", new { }, cancellationToken);
        var wire = Deserialize<ContextWire>(result.Result) ?? throw new ProbeException(ProbeErrorCodes.InvalidResultMessage, "context.get result is missing", "validation");
        return new TribonContextResult(new TribonContext { SessionActive = wire.SessionActive, Module = wire.Module, DatabaseName = wire.Database.Name, Drawing = wire.Drawing, View = wire.View });
    }

    public async Task<AnnotationExportResult> ExportAnnotationsAsync(AnnotationExportRequest request, CancellationToken cancellationToken)
    {
        var result = await SendAsync("annotation.export", request, cancellationToken);
        var export = Deserialize<AnnotationExportWire>(result.Result) ?? throw new ProbeException(ProbeErrorCodes.InvalidResultMessage, "annotation.export result is missing", "validation");
        var context = await GetContextAsync(cancellationToken);
        return new AnnotationExportResult(context.Context, export.Annotations);
    }

    public async Task<MoveAnnotationResult> MoveAnnotationAsync(MoveAnnotationRequest request, CancellationToken cancellationToken)
    {
        var result = await SendAsync("annotation.move", request, cancellationToken);
        return Deserialize<MoveAnnotationResult>(result.Result) ?? throw new ProbeException(ProbeErrorCodes.InvalidResultMessage, "annotation.move result is missing", "validation");
    }

    public async Task<AnnotationValidationResult> ValidateAnnotationAsync(AnnotationValidationRequest request, CancellationToken cancellationToken)
    {
        var export = await ExportAnnotationsAsync(new AnnotationExportRequest(), cancellationToken);
        var item = export.Annotations.FirstOrDefault(a => a.ObjectRef.PersistentId == request.ObjectRef.PersistentId);
        return item is null ? new(false, null, ProbeErrorCodes.ObjectNotFound) : new(item.Position.IsWithinTolerance(request.ExpectedPosition, request.ToleranceMm), item.Position, item.Position.IsWithinTolerance(request.ExpectedPosition, request.ToleranceMm) ? null : ProbeErrorCodes.VerificationFailed);
    }

    public async Task<GeometryObjectDetectionResponse> DetectGeometryObjectsAsync(GeometryObjectDetectionRequest request, CancellationToken cancellationToken) => Deserialize<GeometryObjectDetectionResponse>((await SendAsync("geometry_objects.detect", request, cancellationToken)).Result) ?? throw new ProbeException(ProbeErrorCodes.InvalidResultMessage, "geometry detect result missing", "validation");
    public async Task<GeometryLabelInspectionResponse> InspectGeometryLabelsAsync(GeometryLabelInspectionRequest request, CancellationToken cancellationToken) => Deserialize<GeometryLabelInspectionResponse>((await SendAsync("geometry_labels.inspect", request, cancellationToken)).Result) ?? throw new ProbeException(ProbeErrorCodes.InvalidResultMessage, "geometry label inspect result missing", "validation");
    public async Task<GeometryObjectLabelApplyResponse> ApplyGeometryLabelMovesAsync(GeometryObjectLabelApplyRequest request, CancellationToken cancellationToken)
    { if (!request.AllowWrite) throw new ProbeException(ProbeErrorCodes.InvalidMessage, "allowWrite must be true", "validation"); return Deserialize<GeometryObjectLabelApplyResponse>((await SendAsync("geometry_labels.apply_moves", request, cancellationToken)).Result) ?? throw new ProbeException(ProbeErrorCodes.InvalidResultMessage, "geometry label apply result missing", "validation"); }

    private async Task<BridgeResult> SendAsync(string action, object payload, CancellationToken cancellationToken)
    {
        var command = new BridgeCommand { MessageId = Guid.NewGuid().ToString("N"), CommandId = "CMD-" + Guid.NewGuid().ToString("N"), CorrelationId = "PROBE-" + Guid.NewGuid().ToString("N"), Action = action, Payload = JsonSerializer.SerializeToElement(payload, JsonDefaults.Options), Execution = new BridgeExecutionOptions(0) };
        var result = await _transport.SendAsync(command, cancellationToken);
        if (result.Status == "failed") throw new ProbeException(result.Error?.Code ?? ProbeErrorCodes.TransportFailed, result.Error?.Message ?? "Tribon command failed", result.Error?.Category ?? "execution", result.Error?.Retryable ?? false);
        return result;
    }

    private static T? Deserialize<T>(object? value) => value is null ? default : JsonSerializer.Deserialize<T>(value.ToString()!, JsonDefaults.Options);

    private sealed record ContextWire(bool SessionActive, string Module, DatabaseWire Database, TribonDrawingRef Drawing, TribonViewRef View);
    private sealed record DatabaseWire(string Name);
    private sealed record AnnotationExportWire(string DrawingId, string ViewId, string DrawingRevision, IReadOnlyList<AnnotationSnapshot> Annotations);
}

public sealed class FileBridgeGeometryAutomationAdapter(FileBridgeTransport transport) : IGeometryAutomationAdapter
{
    private readonly FileBridgeTransport _transport = transport;

    public async Task<GeometryDetectionResult> DetectAsync(GeometryDetectionRequest request, CancellationToken cancellationToken)
        => Read<GeometryDetectionResult>(await SendAsync("geometry.detect", request, cancellationToken), "geometry.detect", request.OperationId);

    public async Task<GeometryHighlightResult> HighlightAsync(GeometryHighlightRequest request, CancellationToken cancellationToken)
    {
        var action = request.Categories is not null && request.Categories.Contains(GeometryObjectCategory.PIPE_FLANGE_FRONT)
            ? "geometry.highlight-flanges" : "geometry.highlight-lifting";
        return Read<GeometryHighlightResult>(await SendAsync(action, request, cancellationToken), action, request.OperationId);
    }

    public async Task<GeometryHighlightClearResult> ClearHighlightAsync(GeometryHighlightClearRequest request, CancellationToken cancellationToken)
        => Read<GeometryHighlightClearResult>(await SendAsync("geometry.highlight-clear", request, cancellationToken), "geometry.highlight-clear", request.OperationId);

    public async Task<GeometryLabelPreflightResult> PreflightLabelsAsync(GeometryLabelPreflightRequest request, CancellationToken cancellationToken)
        => Read<GeometryLabelPreflightResult>(await SendAsync("geometry.label-preflight", request, cancellationToken), "geometry.label-preflight", request.OperationId);

    public async Task<GeometryLabelApplyMissingResult> ApplyMissingLabelsAsync(GeometryLabelApplyMissingRequest request, CancellationToken cancellationToken)
    {
        if (!request.AllowWrite) throw new ProbeException(ProbeErrorCodes.InvalidMessage, "allowWrite must be true", "validation");
        return Read<GeometryLabelApplyMissingResult>(await SendAsync("geometry.label-apply-missing", request, cancellationToken), "geometry.label-apply-missing", request.OperationId);
    }

    private async Task<BridgeResult> SendAsync(string action, object payload, CancellationToken cancellationToken)
    {
        var command = new BridgeCommand { MessageId = Guid.NewGuid().ToString("N"), CommandId = "CMD-" + Guid.NewGuid().ToString("N"), CorrelationId = "PROBE-" + Guid.NewGuid().ToString("N"), Action = action, Payload = JsonSerializer.SerializeToElement(payload, JsonDefaults.Options), Execution = new BridgeExecutionOptions(0) };
        var result = await _transport.SendAsync(command, cancellationToken);
        if (result.Status == "failed") throw new ProbeException(result.Error?.Code ?? ProbeErrorCodes.TransportFailed, result.Error?.Message ?? "Tribon command failed", result.Error?.Category ?? "execution", result.Error?.Retryable ?? false);
        return result;
    }

    private static T Read<T>(BridgeResult result, string action, string operationId)
    {
        if (result.Result is null)
            throw new ProbeException(ProbeErrorCodes.InvalidResultMessage, action + " result is missing", "validation");

        using var document = JsonDocument.Parse(result.Result.ToString()!);
        var root = document.RootElement;

        if (!root.TryGetProperty("taskType", out var taskType) ||
            !string.Equals(taskType.GetString(), action, StringComparison.Ordinal))
            throw new ProbeException(ProbeErrorCodes.InvalidResultMessage, action + " result taskType mismatch", "validation");

        if (!root.TryGetProperty("operationId", out var actualOperationId) ||
            !string.Equals(actualOperationId.GetString(), operationId, StringComparison.Ordinal))
            throw new ProbeException(ProbeErrorCodes.InvalidResultMessage, action + " result operationId mismatch", "validation");

        return JsonSerializer.Deserialize<T>(root.GetRawText(), JsonDefaults.Options)
            ?? throw new ProbeException(ProbeErrorCodes.InvalidResultMessage, action + " result is invalid", "validation");
    }
}



