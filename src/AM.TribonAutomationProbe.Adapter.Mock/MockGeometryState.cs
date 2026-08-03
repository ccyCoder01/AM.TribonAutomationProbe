using AM.TribonAutomationProbe.Core;

namespace AM.TribonAutomationProbe.Adapter.Mock;

public sealed record MockTribonGeometryBehavior(string? FailVerificationForOperationId = null, string? FailRollbackForOperationId = null, ManualResetEventSlim? FirstWriteEntered = null, ManualResetEventSlim? ContinueAfterFirstWrite = null);
public sealed class MockTribonGeometryState
{
    public LayoutRectangle DrawingExtent { get; init; } = new(10, 10, 410, 287);
    public IReadOnlyList<DetectedGeometryObject> DetectedObjects { get; init; } = Array.Empty<DetectedGeometryObject>();
    public int CapturedContourCount { get; init; }
    public int UnassignedContourCount { get; init; }
    public List<ExistingGeometryLabel> ExistingLabels { get; } = new();
    public MockTribonGeometryBehavior Behavior { get; init; } = new();
    public MockTribonGeometryState Clone()
    {
        var copy = new MockTribonGeometryState
        {
            DrawingExtent = DrawingExtent,
            DetectedObjects = DetectedObjects.Select(CloneObject).ToArray(),
            Behavior = Behavior,
            CapturedContourCount = CapturedContourCount,
            UnassignedContourCount = UnassignedContourCount
        };
        copy.ExistingLabels.AddRange(ExistingLabels.Select(x => x with { }));
        return copy;
    }

    private static DetectedGeometryObject CloneObject(DetectedGeometryObject value)
    {
        return value with
        {
            SeedHandles = value.SeedHandles.ToArray(),
            GeometryHandles = value.GeometryHandles.ToArray(),
            Features = value.Features with { }
        };
    }
}
