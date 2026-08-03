using System.Security.Cryptography;
using System.Text;
using AM.TribonAutomationProbe.Core;

namespace AM.TribonAutomationProbe.Adapter.Mock;

public sealed class MockTribonAdapter : ITribonAdapter
{
    private readonly object _gate = new();
    private readonly TribonContext _context = new() { SessionActive = true, Module = "drafting", DatabaseName = "MOCK_DB", Drawing = new("DRAWING-001", "MOCK-DRAWING", true, "REV-001"), View = new("VIEW-001", "VIEW-1") };
    private readonly List<AnnotationSnapshot> _annotations = [new() { ObjectRef = new() { ObjectType = "label", PersistentId = "TRIBON-ANN-001", FallbackLocator = new("LABEL-001", 1) }, ObjectType = "label", Text = "103P-CLH2610-002", Position = new(120, 85) }, new() { ObjectRef = new() { ObjectType = "dimension", PersistentId = "TRIBON-ANN-002", FallbackLocator = new("DIM-001", 2) }, ObjectType = "dimension", Text = "2500", Position = new(300, 150) }, new() { ObjectRef = new() { ObjectType = "general_text", PersistentId = "TRIBON-ANN-003", FallbackLocator = new("TEXT-001", 3) }, ObjectType = "general_text", Text = "MOCK NOTE", Position = new(450, 200) }];
    private readonly MockTribonGeometryState _geometry;
    public MockTribonAdapter() : this(new MockTribonGeometryState()) { }
    public MockTribonAdapter(MockTribonGeometryState geometry) { _geometry = geometry; }
    public Task<TribonContextResult> GetContextAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(new TribonContextResult(_context)); }
    public Task<AnnotationExportResult> ExportAnnotationsAsync(AnnotationExportRequest request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); lock (_gate) { var allowed = request.Types.ToHashSet(StringComparer.OrdinalIgnoreCase); return Task.FromResult(new AnnotationExportResult(_context, _annotations.Where(x => allowed.Contains(x.ObjectType)).Select(x => x with { ObjectRef = x.ObjectRef with { Fingerprint = Fingerprint(x) } }).ToArray())); } }
    public Task<MoveAnnotationResult> MoveAnnotationAsync(MoveAnnotationRequest request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); lock (_gate) { var i = Find(request.ObjectRef); if (i < 0) throw new ProbeException(ProbeErrorCodes.ObjectNotFound, "Annotation was not found", "context"); var before = _annotations[i].Position; if (!before.IsWithinTolerance(request.ExpectedPosition, request.PositionToleranceMm)) throw new ProbeException(ProbeErrorCodes.ObjectStateChanged, "Annotation position differs", "concurrency"); _annotations[i] = _annotations[i] with { Position = request.DesiredPosition }; return Task.FromResult(new MoveAnnotationResult(_annotations[i].ObjectRef, before, request.DesiredPosition, request.DesiredPosition, true, true, true, false)); } }
    public Task<AnnotationValidationResult> ValidateAnnotationAsync(AnnotationValidationRequest request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); lock (_gate) { var i = Find(request.ObjectRef); if (i < 0) return Task.FromResult(new AnnotationValidationResult(false, null, ProbeErrorCodes.ObjectNotFound)); return Task.FromResult(new AnnotationValidationResult(true, _annotations[i].Position)); } }
    public Task<GeometryObjectDetectionResponse> DetectGeometryObjectsAsync(GeometryObjectDetectionRequest request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); lock (_gate) { var objects = _geometry.DetectedObjects.Select(CloneObject).ToArray(); var handles = objects.SelectMany(x => x.GeometryHandles).Distinct(StringComparer.Ordinal).Count(); return Task.FromResult(new GeometryObjectDetectionResponse("1.0", request.RequestId, "succeeded", "current_drawing_contours", _geometry.DrawingExtent, objects, new GeometryObjectDetectionDiagnostics(_geometry.CapturedContourCount, handles, _geometry.UnassignedContourCount, 0, 0))); } }
    public Task<GeometryLabelInspectionResponse> InspectGeometryLabelsAsync(GeometryLabelInspectionRequest request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); lock (_gate) { return Task.FromResult(new GeometryLabelInspectionResponse("1.0", request.RequestId, "succeeded", _geometry.ExistingLabels.Select(x => x with { }).ToArray(), new GeometryLabelInspectionDiagnostics())); } }
    public Task<GeometryObjectLabelApplyResponse> ApplyGeometryLabelMovesAsync(GeometryObjectLabelApplyRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var moves = request.Moves ?? Array.Empty<GeometryObjectLabelMove>();
            if (moves.Count == 0) return Task.FromResult(Response(request, "succeeded", Array.Empty<GeometryObjectLabelApplyReceipt>(), false, false, false, null));
            if (!request.AllowWrite) throw Invalid("allowWrite must be true");
            ValidateState();
            var prepared = Prepare(moves);
            ct.ThrowIfCancellationRequested();
            var beforeState = prepared.Select(x => new StateSnapshot(x.Index, x.Before)).ToArray();
            var changed = new List<PreparedGeometryMove>();
            foreach (var item in prepared)
            {
                if (item.AlreadyApplied) continue;
                _geometry.ExistingLabels[item.Index] = item.Before with { Extent = item.Move.DesiredExtent };
                changed.Add(item);
                if (changed.Count == 1)
                {
                    _geometry.Behavior.FirstWriteEntered?.Set();
                    _geometry.Behavior.ContinueAfterFirstWrite?.Wait();
                }
                if (_geometry.Behavior.FailVerificationForOperationId == item.Move.OperationId)
                    return Rollback(request, prepared, changed, beforeState);
            }
            return Task.FromResult(Response(request, "succeeded", prepared.Select(x => new GeometryObjectLabelApplyReceipt(x.OperationId, x.Move.RuntimeHandle, x.AlreadyApplied ? "already_applied" : "applied")).ToArray(), changed.Count > 0, HasNetChange(beforeState), false, null));
        }
    }
    private sealed record PreparedGeometryMove(int RequestIndex, GeometryObjectLabelMove Move, int Index, ExistingGeometryLabel Before, bool AlreadyApplied)
    { public string OperationId => Move.OperationId; }
    private sealed record StateSnapshot(int Index, ExistingGeometryLabel Before);
    private static readonly double ExtentTolerance = 0.01;
    private void ValidateState()
    {
        if (_geometry.ExistingLabels.Any(x => string.IsNullOrWhiteSpace(x.RuntimeHandle) || string.IsNullOrWhiteSpace(x.Text))) throw new ProbeException(ProbeErrorCodes.ObjectStateChanged, "Invalid label state", "concurrency");
        if (_geometry.ExistingLabels.GroupBy(x => x.RuntimeHandle, StringComparer.Ordinal).Any(x => x.Count() > 1)) throw new ProbeException(ProbeErrorCodes.ObjectStateChanged, "Duplicate label runtime handle", "concurrency");
    }
    private List<PreparedGeometryMove> Prepare(IReadOnlyList<GeometryObjectLabelMove> moves)
    {
        if (moves.Any(x => string.IsNullOrWhiteSpace(x.OperationId) || string.IsNullOrWhiteSpace(x.RuntimeHandle) || string.IsNullOrWhiteSpace(x.ExpectedText)) || moves.Select(x => x.OperationId).Distinct(StringComparer.Ordinal).Count() != moves.Count || moves.Select(x => x.RuntimeHandle).Distinct(StringComparer.Ordinal).Count() != moves.Count) throw Invalid("Invalid or duplicate move identity");
        var result = new List<PreparedGeometryMove>();
        for (var i = 0; i < moves.Count; i++) { var move = moves[i]; ValidateRectangle(move.ExpectedExtent); ValidateRectangle(move.DesiredExtent); if (!Finite(move.Dx) || !Finite(move.Dy) || !move.ExpectedExtent.Move(move.Dx, move.Dy).ApproximatelyEquals(move.DesiredExtent, ExtentTolerance)) throw Invalid("Invalid move delta or desired extent"); var index = _geometry.ExistingLabels.FindIndex(x => x.RuntimeHandle == move.RuntimeHandle); if (index < 0) throw new ProbeException(ProbeErrorCodes.ObjectNotFound, "Label not found", "validation"); var before = _geometry.ExistingLabels[index]; if (before.Text != move.ExpectedText) throw new ProbeException(ProbeErrorCodes.ObjectStateChanged, "Text changed", "concurrency"); if (!before.Extent.ApproximatelyEquals(move.ExpectedExtent, ExtentTolerance) && !before.Extent.ApproximatelyEquals(move.DesiredExtent, ExtentTolerance)) throw new ProbeException(ProbeErrorCodes.ObjectStateChanged, "Extent changed", "concurrency"); result.Add(new PreparedGeometryMove(i, move, index, before, before.Extent.ApproximatelyEquals(move.DesiredExtent, ExtentTolerance))); }
        return result;
    }
    private Task<GeometryObjectLabelApplyResponse> Rollback(GeometryObjectLabelApplyRequest request, IReadOnlyList<PreparedGeometryMove> prepared, IReadOnlyList<PreparedGeometryMove> changed, IReadOnlyList<StateSnapshot> beforeState)
    { var failed = false; var statuses = new Dictionary<string, string>(StringComparer.Ordinal); foreach (var item in prepared.Where(x => x.AlreadyApplied)) statuses[item.OperationId] = "already_applied"; foreach (var item in changed.AsEnumerable().Reverse()) { if (_geometry.Behavior.FailRollbackForOperationId == item.OperationId) { failed = true; statuses[item.OperationId] = "rollback_failed"; } else { _geometry.ExistingLabels[item.Index] = item.Before; statuses[item.OperationId] = "rolled_back"; } } foreach (var item in prepared.Where(x => !statuses.ContainsKey(x.OperationId))) statuses[item.OperationId] = "not_attempted"; var receipts = prepared.OrderBy(x => x.RequestIndex).Select(x => new GeometryObjectLabelApplyReceipt(x.OperationId, x.Move.RuntimeHandle, statuses[x.OperationId])).ToArray(); return Task.FromResult(Response(request, failed ? "failed_rollback" : "failed_rolled_back", receipts, true, HasNetChange(beforeState), true, !failed)); }
    private bool HasNetChange(IEnumerable<StateSnapshot> before) => before.Any(x => !_geometry.ExistingLabels[x.Index].Equals(x.Before));
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static void ValidateRectangle(LayoutRectangle value) { if (!Finite(value.X1) || !Finite(value.Y1) || !Finite(value.X2) || !Finite(value.Y2) || value.X2 < value.X1 || value.Y2 < value.Y1) throw Invalid("Invalid rectangle"); }
    private static ProbeException Invalid(string message) => new(ProbeErrorCodes.InvalidMessage, message, "validation");
    private static DetectedGeometryObject CloneObject(DetectedGeometryObject value) => value with { SeedHandles = value.SeedHandles.ToArray(), GeometryHandles = value.GeometryHandles.ToArray(), Features = value.Features with { } };
    private static GeometryObjectLabelApplyResponse Response(GeometryObjectLabelApplyRequest request, string status, IReadOnlyList<GeometryObjectLabelApplyReceipt> receipts, bool attempted, bool net, bool rollback, bool? rollbackSucceeded) => new("1.0", request.RequestId, status, false, receipts, new GeometryObjectApplyDiagnostics(request.AllowWrite, attempted, net, rollback, rollbackSucceeded));
    private int Find(TribonObjectRef r) => _annotations.FindIndex(a => r.PersistentId is not null && a.ObjectRef.PersistentId == r.PersistentId);
    private static string Fingerprint(AnnotationSnapshot a) => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{a.ObjectType}|{a.Text}|{a.Position.X:R}|{a.Position.Y:R}"))).ToLowerInvariant();
}
