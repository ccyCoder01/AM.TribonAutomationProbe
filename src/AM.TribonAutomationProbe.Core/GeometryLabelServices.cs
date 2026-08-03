namespace AM.TribonAutomationProbe.Core;

public enum GeometryLabelMatchStatus { Matched, Missing, Duplicate, TextConflict }
public enum GeometryLabelDirection { Above, Below, Right, Left, Overlapping, Indeterminate }
public enum GeometryLabelPlanAction { KeepCurrent, Move, Blocked }
public sealed record GeometryLabelMatch(GeometryObjectDisplayIdentity Identity, DetectedGeometryObject Object, GeometryLabelMatchStatus Status, ExistingGeometryLabel? Label, IReadOnlyList<ExistingGeometryLabel> SameText);
public sealed record GeometryLabelMatchDiagnostics(int MissingCount, int DuplicateCount, int TextConflictCount);
public sealed record GeometryObjectLabelMatchResult(IReadOnlyList<GeometryLabelMatch> Matches, IReadOnlyList<ExistingGeometryLabel> OtherTexts, GeometryLabelMatchDiagnostics Diagnostics);
public sealed record GeometryLabelAuditItem(string OperationId, string RuntimeObjectId, string DisplayId, GeometryObjectCategory Category, GeometryLabelMatchStatus MatchStatus, string? RuntimeLabelHandle, string? Text, LayoutRectangle ObjectExtent, LayoutRectangle? LabelExtent, GeometryLabelDirection Direction, double OwnObjectOverlapArea, double OwnObjectClearance, IReadOnlyList<string> OtherObjectOverlapIds, IReadOnlyList<string> TargetLabelOverlapIds, IReadOnlyList<string> OtherTextOverlapHandles, string Decision);
public sealed record GeometryObjectLabelAuditResult(string SchemaVersion, string Status, int ObjectCount, int MatchedLabelCount, int MissingLabelCount, int DuplicateLabelCount, int OwnObjectOverlapCount, int OtherObjectOverlapLabelCount, int TargetLabelOverlapLabelCount, int OtherTextOverlapLabelCount, int NeedsRelayoutCount, IReadOnlyList<GeometryLabelAuditItem> Items, int TextConflictCount = 0, int ClearPreferredSideCount = 0, int ClearNonPreferredSideCount = 0, double AreaTolerance = 0.000001, double Clearance = 7.0);
public sealed record GeometryObjectLabelPlanItem(string OperationId, string RuntimeObjectId, string DisplayId, GeometryObjectCategory Category, string? RuntimeLabelHandle, string ExpectedText, LayoutRectangle? CurrentExtent, LayoutRectangle? DesiredExtent, GeometryLabelPlanAction Action, GeometryLabelDirection Direction, double Dx, double Dy, int CurrentConflictCount, int DesiredConflictCount);
public sealed record GeometryObjectLabelPlan(string SchemaVersion, string TaskType, string Mode, string Status, LayoutRectangle DrawingExtent, int InitialConflictCount, int RemainingConflictCount, int KeepCount, int MoveCount, int BlockedCount, bool RequiresAllowWrite, bool DrawingWritePerformed, IReadOnlyList<GeometryObjectLabelPlanItem> Items, IReadOnlyList<GeometryObjectLabelMove> Moves);

public sealed record GeometryLabelLayoutOptions(double Clearance = 7.0, double AreaTolerance = 0.000001);
public sealed record GeometryLabelMatchOptions(double MinimumNeighbourhood = 10.0, double ObjectSizeRatio = 0.5, double MaximumNeighbourhood = 60.0);
public sealed record GeometryObjectLabelPlanningContext(GeometryObjectLabelAuditResult Audit, LayoutRectangle DrawingExtent, IReadOnlyList<DetectedGeometryObject> Objects, IReadOnlyList<GeometryLabelMatch> Matches, IReadOnlyList<ExistingGeometryLabel> OtherTexts);

public static class GeometryObjectLabelMatcher
{
    public static GeometryObjectLabelMatchResult MatchResult(IReadOnlyList<DetectedGeometryObject> objects, GeometryObjectDisplayIdMap ids, IReadOnlyList<ExistingGeometryLabel> labels, GeometryLabelMatchOptions? options = null)
    {
        var matches = Match(objects, ids, labels, options); var matchedHandles = new HashSet<string>(matches.Where(x => x.Status == GeometryLabelMatchStatus.Matched && x.Label is not null).Select(x => x.Label!.RuntimeHandle), StringComparer.Ordinal); var other = labels.Where(x => !matchedHandles.Contains(x.RuntimeHandle)).ToArray(); return new(matches, other, new GeometryLabelMatchDiagnostics(matches.Count(x => x.Status == GeometryLabelMatchStatus.Missing), matches.Count(x => x.Status == GeometryLabelMatchStatus.Duplicate), matches.Count(x => x.Status == GeometryLabelMatchStatus.TextConflict)));
    }
    public static IReadOnlyList<GeometryLabelMatch> Match(IReadOnlyList<DetectedGeometryObject> objects, GeometryObjectDisplayIdMap ids, IReadOnlyList<ExistingGeometryLabel> labels, GeometryLabelMatchOptions? options = null)
    {
        var opt = options ?? new();
        ValidateOptions(opt);
        if (objects.Any(x => string.IsNullOrWhiteSpace(x.RuntimeObjectId)) || objects.Select(x => x.RuntimeObjectId).Distinct(StringComparer.Ordinal).Count() != objects.Count) throw new ProbeException(ProbeErrorCodes.InvalidMessage, "Geometry object ids must be non-empty and unique", "validation");
        if (ids.Items.Any(x => string.IsNullOrWhiteSpace(x.RuntimeObjectId)) || ids.Items.Select(x => x.RuntimeObjectId).Distinct(StringComparer.Ordinal).Count() != ids.Items.Count || ids.Items.Count != objects.Count || ids.Items.Any(x => !objects.Any(o => o.RuntimeObjectId == x.RuntimeObjectId))) throw new ProbeException(ProbeErrorCodes.InvalidMessage, "Display map does not match objects", "validation");
        var result = new List<GeometryLabelMatch>();
        foreach (var identity in ids.Items)
        {
            var obj = objects.First(x => x.RuntimeObjectId == identity.RuntimeObjectId); var same = labels.Where(x => string.Equals(x.Text, identity.DisplayId, StringComparison.Ordinal)).ToArray();
            if (same.Length == 0) result.Add(new(identity, obj, GeometryLabelMatchStatus.Missing, null, same));
            else if (same.Length > 1) result.Add(new(identity, obj, GeometryLabelMatchStatus.Duplicate, null, same));
            else { var label = same[0]; var distance = RectangleDistance(obj.Extent, label.Extent); var limit = Math.Min(opt.MaximumNeighbourhood, Math.Max(opt.MinimumNeighbourhood, Math.Sqrt(obj.Extent.Width * obj.Extent.Width + obj.Extent.Height * obj.Extent.Height) * opt.ObjectSizeRatio)); result.Add(new(identity, obj, distance <= limit ? GeometryLabelMatchStatus.Matched : GeometryLabelMatchStatus.TextConflict, label, same)); }
        }
        return result;
    }
    private static void ValidateOptions(GeometryLabelMatchOptions opt) { if (!Finite(opt.MinimumNeighbourhood) || !Finite(opt.ObjectSizeRatio) || !Finite(opt.MaximumNeighbourhood) || opt.MinimumNeighbourhood < 0 || opt.ObjectSizeRatio < 0 || opt.MaximumNeighbourhood < 0 || opt.MaximumNeighbourhood < opt.MinimumNeighbourhood) throw new ProbeException(ProbeErrorCodes.InvalidMessage, "Invalid label match options", "validation"); }
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static double RectangleDistance(LayoutRectangle a, LayoutRectangle b) { var dx = Math.Max(Math.Max(a.X1 - b.X2, b.X1 - a.X2), 0); var dy = Math.Max(Math.Max(a.Y1 - b.Y2, b.Y1 - a.Y2), 0); return Math.Sqrt(dx * dx + dy * dy); }
}

public static class GeometryObjectLabelAuditService
{
    public static GeometryObjectLabelAuditResult Audit(GeometryObjectLabelMatchResult result, IReadOnlyList<DetectedGeometryObject> allObjects, GeometryLabelLayoutOptions? options = null) => AuditCore(result.Matches, result.OtherTexts, allObjects, options);
    public static GeometryObjectLabelAuditResult Audit(IReadOnlyList<GeometryLabelMatch> matches, IReadOnlyList<ExistingGeometryLabel> labels, GeometryLabelLayoutOptions? options = null)
        => AuditCore(matches, labels.Where(x => !matches.Any(m => m.Label?.RuntimeHandle == x.RuntimeHandle)).ToArray(), matches.Select(x => x.Object).ToArray(), options);
    private static GeometryObjectLabelAuditResult AuditCore(IReadOnlyList<GeometryLabelMatch> matches, IReadOnlyList<ExistingGeometryLabel> otherTexts, IReadOnlyList<DetectedGeometryObject> allObjects, GeometryLabelLayoutOptions? options = null)
    {
        var opt = options ?? new(); ValidateLayoutOptions(opt); var items = new List<GeometryLabelAuditItem>();
        foreach (var match in matches)
        {
            var label = match.Label; var own = label is null ? 0 : match.Object.Extent.IntersectionArea(label.Extent); var direction = Direction(match.Object.Extent, label?.Extent, opt.AreaTolerance); var otherObjects = new List<string>(); var otherLabels = new List<string>(); var otherText = new List<string>();
            if (label is not null) foreach (var other in allObjects.Where(x => x.RuntimeObjectId != match.Object.RuntimeObjectId)) if (label.Extent.OverlapsByArea(other.Extent, opt.AreaTolerance)) otherObjects.Add(other.RuntimeObjectId);
            if (label is not null) foreach (var other in matches.Where(x => x != match && x.Label is not null)) if (label.Extent.OverlapsByArea(other.Label!.Extent, opt.AreaTolerance)) otherLabels.Add(other.Identity.DisplayId);
            foreach (var text in otherTexts) if (label is not null && label.Extent.OverlapsByArea(text.Extent, opt.AreaTolerance)) otherText.Add(text.RuntimeHandle);
            var decision = match.Status != GeometryLabelMatchStatus.Matched ? (match.Status == GeometryLabelMatchStatus.TextConflict ? "blocked_text_conflict" : "blocked_" + match.Status.ToString().ToLowerInvariant()) : own > opt.AreaTolerance || otherObjects.Count > 0 || otherLabels.Count > 0 || otherText.Count > 0 || direction == GeometryLabelDirection.Indeterminate || direction == GeometryLabelDirection.Overlapping ? "needs_relayout" : direction == GeometryLabelDirection.Above ? "clear_preferred_side" : "clear_non_preferred_side";
            items.Add(new("label:" + match.Identity.DisplayId, match.Identity.RuntimeObjectId, match.Identity.DisplayId, match.Identity.Category, match.Status, label?.RuntimeHandle, label?.Text, match.Object.Extent, label?.Extent, direction, own, Clearance(match.Object.Extent, label?.Extent, direction), otherObjects, otherLabels, otherText, decision));
        }
        var blocked = items.Any(x => x.MatchStatus != GeometryLabelMatchStatus.Matched); return new("1.0", blocked ? "blocked" : "succeeded", items.Count, items.Count(x => x.MatchStatus == GeometryLabelMatchStatus.Matched), items.Count(x => x.MatchStatus == GeometryLabelMatchStatus.Missing), items.Count(x => x.MatchStatus == GeometryLabelMatchStatus.Duplicate), items.Count(x => x.OwnObjectOverlapArea > opt.AreaTolerance), items.Count(x => x.OtherObjectOverlapIds.Count > 0), items.Count(x => x.TargetLabelOverlapIds.Count > 0), items.Count(x => x.OtherTextOverlapHandles.Count > 0), items.Count(x => x.Decision == "needs_relayout"), items, items.Count(x => x.MatchStatus == GeometryLabelMatchStatus.TextConflict), items.Count(x => x.Decision == "clear_preferred_side"), items.Count(x => x.Decision == "clear_non_preferred_side"), opt.AreaTolerance, opt.Clearance);
    }
    private static double Clearance(LayoutRectangle a, LayoutRectangle? b, GeometryLabelDirection d) { if (b is null || d == GeometryLabelDirection.Overlapping || d == GeometryLabelDirection.Indeterminate) return 0; var value = d == GeometryLabelDirection.Above ? b.Y1 - a.Y2 : d == GeometryLabelDirection.Below ? a.Y1 - b.Y2 : d == GeometryLabelDirection.Right ? b.X1 - a.X2 : a.X1 - b.X2; return Math.Abs(value) < 0.01 ? 0 : Math.Max(0, value); }
    private static void ValidateLayoutOptions(GeometryLabelLayoutOptions x) { if (!Finite(x.Clearance) || !Finite(x.AreaTolerance) || x.Clearance < 0 || x.AreaTolerance < 0) throw new ProbeException(ProbeErrorCodes.InvalidMessage, "Invalid layout options", "validation"); }
    private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    private static GeometryLabelDirection Direction(LayoutRectangle obj, LayoutRectangle? label, double tolerance) { if (label is null) return GeometryLabelDirection.Indeterminate; if (obj.IntersectionArea(label) > tolerance) return GeometryLabelDirection.Overlapping; if (label.Y1 >= obj.Y2) return GeometryLabelDirection.Above; if (label.Y2 <= obj.Y1) return GeometryLabelDirection.Below; if (label.X1 >= obj.X2) return GeometryLabelDirection.Right; if (label.X2 <= obj.X1) return GeometryLabelDirection.Left; return GeometryLabelDirection.Indeterminate; }
}

public static class GeometryObjectLabelLayoutPlanner
{
    public static GeometryObjectLabelPlan Plan(GeometryObjectLabelPlanningContext context, GeometryLabelLayoutOptions? options = null)
    {
        var opt = options ?? new(); ValidateOptions(opt);
        ValidateContext(context);
        ValidateContextMappings(context);
        if (context.Audit.Status == "blocked") return BlockedPlan(context, context.Audit.Items, opt.AreaTolerance);
        var items = context.Audit.Items.ToDictionary(x => x.OperationId, StringComparer.Ordinal); var working = context.Matches.Where(x => x.Label is not null).ToDictionary(x => x.Label!.RuntimeHandle, x => x.Label!.Extent, StringComparer.Ordinal); var target = context.Matches.Where(x => x.Status == GeometryLabelMatchStatus.Matched && x.Label is not null && items["label:" + x.Identity.DisplayId].Decision == "needs_relayout").OrderByDescending(x => ConflictCount(items["label:" + x.Identity.DisplayId], opt.AreaTolerance)).ThenBy(x => x.Identity.Category).ThenBy(x => x.Identity.DisplayId, StringComparer.Ordinal).ThenBy(x => "label:" + x.Identity.DisplayId, StringComparer.Ordinal).ToArray();
        var output = new Dictionary<string, GeometryObjectLabelPlanItem>(StringComparer.Ordinal); var moves = new List<GeometryObjectLabelMove>(); var failed = false; foreach (var item in context.Audit.Items) if (item.MatchStatus != GeometryLabelMatchStatus.Matched) output[item.OperationId] = ToBlocked(item, opt.AreaTolerance);
        foreach (var item in context.Audit.Items.Where(x => x.MatchStatus == GeometryLabelMatchStatus.Matched && !target.Any(t => t.Identity.DisplayId == x.DisplayId))) { var count = ConflictCount(item, opt.AreaTolerance); if (item.Decision == "needs_relayout") { failed = true; output[item.OperationId] = ToNoSolution(item, count); } else output[item.OperationId] = ToKeep(item, count); }
        foreach (var current in target)
        {
            var audit = items["label:" + current.Identity.DisplayId]; var label = current.Label!; var chosen = Candidates(current.Object.Extent, label.Extent, opt.Clearance).FirstOrDefault(c => CountCandidateConflicts(c.Extent, current, context, working, opt) == 0); var currentConflicts = ConflictCount(audit, opt.AreaTolerance);
            var direction = chosen.Direction; var extent = chosen.Extent; if (extent.Equals(default(LayoutRectangle))) { output[audit.OperationId] = ToNoSolution(audit, currentConflicts); failed = true; continue; }
            var desiredConflicts = CountCandidateConflicts(extent, current, context, working, opt); output[audit.OperationId] = new(audit.OperationId, audit.RuntimeObjectId, audit.DisplayId, audit.Category, audit.RuntimeLabelHandle, audit.Text ?? audit.DisplayId, label.Extent, extent, GeometryLabelPlanAction.Move, direction, extent.X1 - label.Extent.X1, extent.Y1 - label.Extent.Y1, currentConflicts, desiredConflicts); working[label.RuntimeHandle] = extent; moves.Add(new(audit.OperationId, label.RuntimeHandle, audit.Text ?? audit.DisplayId, label.Extent, extent, extent.X1 - label.Extent.X1, extent.Y1 - label.Extent.Y1));
        }
        if (failed) { moves.Clear(); foreach (var key in output.Keys.ToArray()) if (output[key].Action == GeometryLabelPlanAction.Move) output[key] = ToNoSolution(items[key], ConflictCount(items[key], opt.AreaTolerance)); }
        var ordered = context.Audit.Items.Select(x => output[x.OperationId]).ToArray(); var initial = context.Audit.Items.Sum(x => ConflictCount(x, opt.AreaTolerance)); var remaining = failed ? initial : ordered.Sum(x => x.DesiredConflictCount); var status = failed ? "no_solution" : moves.Count == 0 ? "no_changes_required" : "planned"; return new("1.0", "create_geometry_object_label_layout", "dry-run", status, context.DrawingExtent, initial, remaining, ordered.Count(x => x.Action == GeometryLabelPlanAction.KeepCurrent), moves.Count, ordered.Count(x => x.Action == GeometryLabelPlanAction.Blocked), moves.Count > 0, false, ordered, moves);
    }
    public static GeometryObjectLabelPlan Plan(GeometryObjectLabelAuditResult audit, LayoutRectangle drawing, GeometryLabelLayoutOptions? options = null)
    {
        if (audit.Items.Any(x => x.Decision == "needs_relayout")) throw new ProbeException(ProbeErrorCodes.InvalidMessage, "GeometryObjectLabelPlanningContext is required for relayout planning", "validation");
        var opt = options ?? new(); var output = new List<GeometryObjectLabelPlanItem>(); var moves = new List<GeometryObjectLabelMove>();
        foreach (var item in audit.Items)
        {
            if (item.MatchStatus != GeometryLabelMatchStatus.Matched) { output.Add(new(item.OperationId, item.RuntimeObjectId, item.DisplayId, item.Category, item.RuntimeLabelHandle, item.DisplayId, item.LabelExtent, null, GeometryLabelPlanAction.Blocked, item.Direction, 0, 0, 1, 1)); continue; }
            if (item.Decision != "needs_relayout") { output.Add(new(item.OperationId, item.RuntimeObjectId, item.DisplayId, item.Category, item.RuntimeLabelHandle, item.Text ?? item.DisplayId, item.LabelExtent, item.LabelExtent, GeometryLabelPlanAction.KeepCurrent, item.Direction, 0, 0, 0, 0)); continue; }
            var label = item.LabelExtent!; var centerX = item.ObjectExtent.CenterX; var desired = new LayoutRectangle(centerX - label.Width / 2, item.ObjectExtent.Y1 - opt.Clearance - label.Height, centerX + label.Width / 2, item.ObjectExtent.Y1 - opt.Clearance); var dx = desired.X1 - label.X1; var dy = desired.Y1 - label.Y1;
            if (!desired.Inside(drawing)) { output.Add(new(item.OperationId, item.RuntimeObjectId, item.DisplayId, item.Category, item.RuntimeLabelHandle, item.Text ?? item.DisplayId, label, null, GeometryLabelPlanAction.Blocked, item.Direction, 0, 0, 1, 1)); continue; }
            output.Add(new(item.OperationId, item.RuntimeObjectId, item.DisplayId, item.Category, item.RuntimeLabelHandle, item.Text ?? item.DisplayId, label, desired, GeometryLabelPlanAction.Move, GeometryLabelDirection.Below, dx, dy, 1, 0)); moves.Add(new(item.OperationId, item.RuntimeLabelHandle!, item.Text ?? item.DisplayId, label, desired, dx, dy));
        }
        var blocked = output.Count(x => x.Action == GeometryLabelPlanAction.Blocked); var moveCount = moves.Count; var status = blocked > 0 ? "blocked" : moveCount == 0 ? "no_changes_required" : "planned";
        return new("1.0", "create_geometry_object_label_layout", "dry-run", status, drawing, audit.NeedsRelayoutCount, 0, output.Count(x => x.Action == GeometryLabelPlanAction.KeepCurrent), moveCount, blocked, moveCount > 0, false, output, moves);
    }
    private static void ValidateOptions(GeometryLabelLayoutOptions x) { if (double.IsNaN(x.Clearance) || double.IsInfinity(x.Clearance) || double.IsNaN(x.AreaTolerance) || double.IsInfinity(x.AreaTolerance) || x.Clearance < 0 || x.AreaTolerance < 0) throw new ProbeException(ProbeErrorCodes.InvalidMessage, "Invalid layout options", "validation"); }
    private static int ConflictCount(GeometryLabelAuditItem x, double tolerance) => (x.OwnObjectOverlapArea > tolerance ? 1 : 0) + x.OtherObjectOverlapIds.Count + x.TargetLabelOverlapIds.Count + x.OtherTextOverlapHandles.Count;
    private static GeometryObjectLabelPlanItem ToKeep(GeometryLabelAuditItem x, int conflicts) => new(x.OperationId, x.RuntimeObjectId, x.DisplayId, x.Category, x.RuntimeLabelHandle, x.Text ?? x.DisplayId, x.LabelExtent, x.LabelExtent, GeometryLabelPlanAction.KeepCurrent, x.Direction, 0, 0, conflicts, conflicts);
    private static GeometryObjectLabelPlanItem ToBlocked(GeometryLabelAuditItem x, double tolerance) => new(x.OperationId, x.RuntimeObjectId, x.DisplayId, x.Category, x.RuntimeLabelHandle, x.Text ?? x.DisplayId, x.LabelExtent, null, GeometryLabelPlanAction.Blocked, x.Direction, 0, 0, ConflictCount(x, tolerance), ConflictCount(x, tolerance));
    private static GeometryObjectLabelPlanItem ToNoSolution(GeometryLabelAuditItem x, int conflicts) => new(x.OperationId, x.RuntimeObjectId, x.DisplayId, x.Category, x.RuntimeLabelHandle, x.Text ?? x.DisplayId, x.LabelExtent, null, GeometryLabelPlanAction.Blocked, x.Direction, 0, 0, conflicts, conflicts);
    private static GeometryObjectLabelPlan BlockedPlan(GeometryObjectLabelPlanningContext c, IReadOnlyList<GeometryLabelAuditItem> items, double tolerance) { var output = items.Select(x => ToBlocked(x, tolerance)).ToArray(); return new("1.0", "create_geometry_object_label_layout", "dry-run", "blocked", c.DrawingExtent, items.Sum(x => ConflictCount(x, tolerance)), items.Sum(x => ConflictCount(x, tolerance)), 0, 0, output.Length, false, false, output, Array.Empty<GeometryObjectLabelMove>()); }
    private static IEnumerable<(GeometryLabelDirection Direction, LayoutRectangle Extent)> Candidates(LayoutRectangle o, LayoutRectangle l, double clearance) { var x = o.CenterX - l.Width / 2; yield return (GeometryLabelDirection.Above, new(x, o.Y2 + clearance, x + l.Width, o.Y2 + clearance + l.Height)); var y = o.Y1 - clearance - l.Height; yield return (GeometryLabelDirection.Below, new(x, y, x + l.Width, y + l.Height)); var ry = o.CenterY - l.Height / 2; yield return (GeometryLabelDirection.Right, new(o.X2 + clearance, ry, o.X2 + clearance + l.Width, ry + l.Height)); var lx = o.X1 - clearance - l.Width; yield return (GeometryLabelDirection.Left, new(lx, ry, lx + l.Width, ry + l.Height)); }
    private static bool Valid(LayoutRectangle c, GeometryLabelMatch current, GeometryObjectLabelPlanningContext context, IReadOnlyDictionary<string, LayoutRectangle> working, GeometryLabelLayoutOptions opt) { if (!c.Inside(context.DrawingExtent) || c.OverlapsByArea(current.Object.Extent, opt.AreaTolerance)) return false; foreach (var o in context.Objects) if (c.OverlapsByArea(o.Extent, opt.AreaTolerance)) return false; foreach (var m in context.Matches.Where(x => x.Label is not null && x.Label.RuntimeHandle != current.Label!.RuntimeHandle)) if (c.OverlapsByArea(working[m.Label!.RuntimeHandle], opt.AreaTolerance)) return false; return context.OtherTexts.All(x => !c.OverlapsByArea(x.Extent, opt.AreaTolerance)); }
    private static int CountCandidateConflicts(LayoutRectangle c, GeometryLabelMatch current, GeometryObjectLabelPlanningContext context, IReadOnlyDictionary<string, LayoutRectangle> working, GeometryLabelLayoutOptions opt) { var n = c.OverlapsByArea(current.Object.Extent, opt.AreaTolerance) ? 1 : 0; n += context.Objects.Count(o => o.RuntimeObjectId != current.Object.RuntimeObjectId && c.OverlapsByArea(o.Extent, opt.AreaTolerance)); n += context.Matches.Count(m => m.Label is not null && m.Label.RuntimeHandle != current.Label!.RuntimeHandle && c.OverlapsByArea(working[m.Label!.RuntimeHandle], opt.AreaTolerance)); n += context.OtherTexts.Count(t => c.OverlapsByArea(t.Extent, opt.AreaTolerance)); return n; }
    private static void ValidateContext(GeometryObjectLabelPlanningContext c)
    {
        if (c is null || c.Audit is null || c.Objects is null || c.Matches is null || c.OtherTexts is null || !Finite(c.DrawingExtent) || c.DrawingExtent.X2 < c.DrawingExtent.X1 || c.DrawingExtent.Y2 < c.DrawingExtent.Y1 || c.Audit.Items is null || c.Audit.Items.Count != c.Matches.Count || c.Audit.Items.Select(x => x.OperationId).Distinct(StringComparer.Ordinal).Count() != c.Audit.Items.Count || c.Objects.Any(x => string.IsNullOrWhiteSpace(x.RuntimeObjectId)) || c.Objects.Select(x => x.RuntimeObjectId).Distinct(StringComparer.Ordinal).Count() != c.Objects.Count || c.Matches.Select(x => x.Identity.RuntimeObjectId).Distinct(StringComparer.Ordinal).Count() != c.Matches.Count || c.Matches.Select(x => x.Identity.DisplayId).Distinct(StringComparer.Ordinal).Count() != c.Matches.Count || c.Matches.Any(x => x.Identity.RuntimeObjectId != x.Object.RuntimeObjectId || x.Identity.Category != x.Object.Category || !c.Objects.Any(o => o.RuntimeObjectId == x.Object.RuntimeObjectId) || (x.Status == GeometryLabelMatchStatus.Matched && (x.Label is null || string.IsNullOrWhiteSpace(x.Label.RuntimeHandle)))) || c.Matches.Where(x => x.Label is not null).Select(x => x.Label!.RuntimeHandle).Distinct(StringComparer.Ordinal).Count() != c.Matches.Count(x => x.Label is not null) || c.OtherTexts.Any(x => string.IsNullOrWhiteSpace(x.RuntimeHandle) || !Finite(x.Extent)) || c.OtherTexts.Select(x => x.RuntimeHandle).Distinct(StringComparer.Ordinal).Count() != c.OtherTexts.Count || c.OtherTexts.Any(x => c.Matches.Any(m => m.Status == GeometryLabelMatchStatus.Matched && m.Label!.RuntimeHandle == x.RuntimeHandle))) throw new ProbeException(ProbeErrorCodes.InvalidMessage, "Invalid planning context", "validation");
    }
    private static bool Finite(LayoutRectangle x) => !double.IsNaN(x.X1) && !double.IsNaN(x.Y1) && !double.IsNaN(x.X2) && !double.IsNaN(x.Y2) && !double.IsInfinity(x.X1) && !double.IsInfinity(x.Y1) && !double.IsInfinity(x.X2) && !double.IsInfinity(x.Y2);
    private static void ValidateContextMappings(GeometryObjectLabelPlanningContext c)
    {
        foreach (var match in c.Matches)
        {
            var candidates = c.Audit.Items.Where(x => x.OperationId == "label:" + match.Identity.DisplayId).ToArray();
            if (candidates.Length != 1) throw new ProbeException(ProbeErrorCodes.InvalidMessage, "Planning context audit mapping is incomplete", "validation");
            var item = candidates[0];
            if (item.RuntimeObjectId != match.Identity.RuntimeObjectId || item.DisplayId != match.Identity.DisplayId || item.Category != match.Identity.Category || item.MatchStatus != match.Status || item.RuntimeLabelHandle != match.Label?.RuntimeHandle || !item.ObjectExtent.Equals(match.Object.Extent)) throw new ProbeException(ProbeErrorCodes.InvalidMessage, "Planning context audit mapping does not match", "validation");
        }
    }
}
