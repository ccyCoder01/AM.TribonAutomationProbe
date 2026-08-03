namespace AM.TribonAutomationProbe.Core;

public sealed record GeometryObjectDisplayIdentity(string RuntimeObjectId, GeometryObjectCategory Category, string DisplayId, bool PersistentAcrossGeometryChanges = false, string HandleScope = "current_drafting_session_only");
public sealed record GeometryObjectDisplayIdMap(IReadOnlyList<GeometryObjectDisplayIdentity> Items, string Status = "succeeded");

public static class GeometryObjectDisplayIdAssigner
{
    private static readonly GeometryObjectCategory[] Order = { GeometryObjectCategory.LIFTING_BEAM, GeometryObjectCategory.LIFTING_LUG, GeometryObjectCategory.PIPE_FLANGE_FRONT, GeometryObjectCategory.PIPE_FLANGE_SIDE, GeometryObjectCategory.STRUCTURAL_FLANGE };
    public static GeometryObjectDisplayIdMap Assign(IEnumerable<DetectedGeometryObject> objects)
    {
        var snapshot = objects.ToArray();
        if (snapshot.Any(x => string.IsNullOrWhiteSpace(x.RuntimeObjectId) || !Finite(x.Extent) || x.Extent.X2 < x.Extent.X1 || x.Extent.Y2 < x.Extent.Y1)) throw new ArgumentException("Geometry object identity or extent is invalid.");
        if (snapshot.Select(x => x.RuntimeObjectId).Distinct(StringComparer.Ordinal).Count() != snapshot.Length) throw new ArgumentException("RuntimeObjectId must be unique.");
        var output = new List<GeometryObjectDisplayIdentity>();
        foreach (var category in Order)
        {
            var prefix = category switch { GeometryObjectCategory.LIFTING_BEAM => "LB", GeometryObjectCategory.LIFTING_LUG => "LL", GeometryObjectCategory.PIPE_FLANGE_FRONT => "PF", GeometryObjectCategory.PIPE_FLANGE_SIDE => "PF-SIDE", GeometryObjectCategory.STRUCTURAL_FLANGE => "SF", _ => throw new ArgumentOutOfRangeException() };
            var index = 1;
            foreach (var item in snapshot.Where(x => x.Category == category).OrderBy(x => x.Extent.CenterX).ThenByDescending(x => x.Extent.CenterY).ThenBy(x => x.Extent.Width).ThenBy(x => x.Extent.Height).ThenBy(x => x.RuntimeObjectId, StringComparer.Ordinal))
            {
                output.Add(new(item.RuntimeObjectId, category, prefix + "-" + index.ToString("00")));
                index++;
            }
        }
        if (output.Select(x => x.DisplayId).Distinct(StringComparer.Ordinal).Count() != output.Count) throw new ArgumentException("DisplayId must be unique.");
        return new(output);
    }
    private static bool Finite(LayoutRectangle x) => double.IsFinite(x.X1) && double.IsFinite(x.Y1) && double.IsFinite(x.X2) && double.IsFinite(x.Y2);
}
