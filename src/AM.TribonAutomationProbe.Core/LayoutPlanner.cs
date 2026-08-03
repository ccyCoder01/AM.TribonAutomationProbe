using System.Text.Json;

namespace AM.TribonAutomationProbe.Core;

public sealed record LayoutRectangle(double X1, double Y1, double X2, double Y2)
{
    public double Width => Math.Max(0, X2 - X1);
    public double Height => Math.Max(0, Y2 - Y1);
    public double CenterX => (X1 + X2) / 2;
    public double CenterY => (Y1 + Y2) / 2;
    public LayoutRectangle Move(double dx, double dy) => new(X1 + dx, Y1 + dy, X2 + dx, Y2 + dy);
    public bool Inside(LayoutRectangle outer) => X1 >= outer.X1 && Y1 >= outer.Y1 && X2 <= outer.X2 && Y2 <= outer.Y2;
    public double IntersectionArea(LayoutRectangle other) => Math.Max(0, Math.Min(X2, other.X2) - Math.Max(X1, other.X1)) * Math.Max(0, Math.Min(Y2, other.Y2) - Math.Max(Y1, other.Y1));
    public bool OverlapsByArea(LayoutRectangle other, double tolerance = 0) => IntersectionArea(other) > tolerance;
    public bool ApproximatelyEquals(LayoutRectangle other, double tolerance = 0.01) => Math.Abs(X1 - other.X1) <= tolerance && Math.Abs(Y1 - other.Y1) <= tolerance && Math.Abs(X2 - other.X2) <= tolerance && Math.Abs(Y2 - other.Y2) <= tolerance;
}

public sealed record AnnotationLayoutItem
{
    public required string Role { get; init; }
    public required string Type { get; init; }
    public required string RuntimeHandle { get; init; }
    public required LayoutRectangle ParentExtent { get; init; }
    public required LayoutRectangle LabelExtent { get; init; }
    public string? Text { get; init; }
    public IReadOnlyList<string> ChildTextHandles { get; init; } = Array.Empty<string>();
}

public sealed record AnnotationLayoutSnapshot
{
    public string SchemaVersion { get; init; } = "1.0";
    public string Scope { get; init; } = "current_drafting_context";
    public string HandleScope { get; init; } = "current_drafting_session_only";
    public required LayoutRectangle DrawingExtent { get; init; }
    public IReadOnlyList<AnnotationLayoutItem> Items { get; init; } = Array.Empty<AnnotationLayoutItem>();
}

public sealed record AnnotationLayoutOptions(double ClearanceMm = 0.5, double GridStepMm = 0.5, double MaximumSearchDistanceMm = 20.0);
public sealed record AnnotationCollision(string FirstHandle, string SecondHandle);
public sealed record AnnotationMoveCandidate(double Dx, double Dy, double Distance);
public sealed record AnnotationLayoutPlanItem(string RuntimeHandle, string Type, string? Text, double Dx, double Dy, double Distance, int ConflictsBefore, int ConflictsAfter);
public sealed record AnnotationLayoutPlan(string SchemaVersion, string SourceSnapshotId, AnnotationLayoutOptions Options, int InitialConflictCount, int RemainingConflictCount, IReadOnlyList<AnnotationLayoutPlanItem> Moves, string Status);

public static class AnnotationLayoutPlanner
{
    public static AnnotationLayoutPlan Plan(AnnotationLayoutSnapshot snapshot, AnnotationLayoutOptions? options = null, string sourceSnapshotId = "snapshot")
    {
        var opt = options ?? new();
        var items = snapshot.Items.Select((item, index) => new WorkingItem(item, index)).ToList();
        var initial = CountConflicts(items, opt.ClearanceMm);
        var moves = new List<AnnotationLayoutPlanItem>();
        var ordered = items.Where(x => x.Item.Role == "movable").OrderByDescending(x => ConflictCount(x, items, opt.ClearanceMm)).ThenBy(x => x.Item.Type, StringComparer.Ordinal).ThenBy(x => x.Item.Text ?? "", StringComparer.Ordinal).ThenBy(x => x.Index).ToList();
        foreach (var target in ordered)
        {
            var before = ConflictCount(target, items, opt.ClearanceMm);
            if (before == 0) continue;
            var candidate = FindCandidate(target, items, snapshot.DrawingExtent, opt);
            if (candidate is null) continue;
            target.Item = target.Item with { LabelExtent = target.Item.LabelExtent.Move(candidate.Dx, candidate.Dy), ParentExtent = target.Item.ParentExtent.Move(candidate.Dx, candidate.Dy) };
            moves.Add(new(target.Item.RuntimeHandle, target.Item.Type, target.Item.Text, candidate.Dx, candidate.Dy, candidate.Distance, before, ConflictCount(target, items, opt.ClearanceMm)));
        }
        var remaining = CountConflicts(items, opt.ClearanceMm);
        var status = initial == 0 ? "no_changes_required" : moves.Count == 0 ? "no_solution" : remaining == 0 ? "planned" : "partial";
        return new("1.0", sourceSnapshotId, opt, initial, remaining, moves, status);
    }

    private static AnnotationMoveCandidate? FindCandidate(WorkingItem target, List<WorkingItem> items, LayoutRectangle drawing, AnnotationLayoutOptions opt)
    {
        var max = (int)Math.Floor(opt.MaximumSearchDistanceMm / opt.GridStepMm);
        var candidates = new List<AnnotationMoveCandidate>();
        for (var ix = -max; ix <= max; ix++) for (var iy = -max; iy <= max; iy++)
        {
            var dx = ix * opt.GridStepMm; var dy = iy * opt.GridStepMm;
            if (ix == 0 && iy == 0) continue;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > opt.MaximumSearchDistanceMm) continue;
            candidates.Add(new(dx, dy, distance));
        }
        foreach (var candidate in candidates.OrderBy(x => x.Distance).ThenBy(x => Math.Abs(x.Dx) + Math.Abs(x.Dy)).ThenBy(x => Math.Abs(x.Dx)).ThenBy(x => Math.Abs(x.Dy)).ThenBy(x => x.Dx).ThenBy(x => x.Dy))
        {
            var moved = target.Item.LabelExtent.Move(candidate.Dx, candidate.Dy);
            if (!target.Item.ParentExtent.Move(candidate.Dx, candidate.Dy).Inside(drawing)) continue;
            if (items.Where(x => x != target).Any(x => (x.Item.Role == "obstacle" || x.Item.Role == "movable") && Collides(moved, x.Item.LabelExtent, opt.ClearanceMm))) continue;
            if (ConflictCount(target, items, opt.ClearanceMm, moved) == 0) return candidate;
        }
        return null;
    }

    private static int CountConflicts(List<WorkingItem> items, double clearance) => items.Sum(x => ConflictCount(x, items, clearance)) / 2;
    private static int ConflictCount(WorkingItem target, List<WorkingItem> items, double clearance, LayoutRectangle? extent = null) => items.Where(x => x != target && (x.Item.Role == "obstacle" || x.Item.Role == "movable")).Count(x => Collides(extent ?? target.Item.LabelExtent, x.Item.LabelExtent, clearance));
    private static bool Collides(LayoutRectangle a, LayoutRectangle b, double clearance) => Math.Min(a.X2, b.X2) - Math.Max(a.X1, b.X1) > -clearance && Math.Min(a.Y2, b.Y2) - Math.Max(a.Y1, b.Y1) > -clearance;
    private sealed class WorkingItem { public WorkingItem(AnnotationLayoutItem item, int index) { Item = item; Index = index; } public AnnotationLayoutItem Item; public int Index; }
}

public static class LayoutJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public static AnnotationLayoutSnapshot ReadSnapshot(string path) => JsonSerializer.Deserialize<AnnotationLayoutSnapshot>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("Snapshot is empty.");
    public static void WritePlan(string path, AnnotationLayoutPlan plan) => File.WriteAllText(path, JsonSerializer.Serialize(plan, Options));
}
