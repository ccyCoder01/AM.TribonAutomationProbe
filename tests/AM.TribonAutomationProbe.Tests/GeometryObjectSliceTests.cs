using AM.TribonAutomationProbe.Core;
using Xunit;

namespace AM.TribonAutomationProbe.Tests;

public sealed class GeometryObjectSliceTests
{
    private static DetectedGeometryObject Obj(string id, GeometryObjectCategory category, double x, double y) => new(id, category, "high", new LayoutRectangle(x, y, x + 10, y + 10), new[] { id }, new[] { id }, 1, new(1));

    [Fact]
    public void DisplayIdsIncrementWithinEachCategoryAndAreOrderIndependent()
    {
        var objects = new[] { Obj("B", GeometryObjectCategory.LIFTING_BEAM, 20, 10), Obj("A", GeometryObjectCategory.LIFTING_BEAM, 10, 10), Obj("L", GeometryObjectCategory.LIFTING_LUG, 1, 1) };
        var map = GeometryObjectDisplayIdAssigner.Assign(objects.Reverse().ToArray());
        Assert.Equal("LB-01", map.Items.Single(x => x.RuntimeObjectId == "A").DisplayId);
        Assert.Equal("LB-02", map.Items.Single(x => x.RuntimeObjectId == "B").DisplayId);
        Assert.Equal("LL-01", map.Items.Single(x => x.RuntimeObjectId == "L").DisplayId);
    }

    [Fact]
    public void LabelPlannerUsesFirstAvailablePreferredDirection()
    {
        var obj = Obj("LB-02", GeometryObjectCategory.LIFTING_BEAM, 52, 169) with { Extent = new LayoutRectangle(52, 169, 168, 209.5) };
        var label = new ExistingGeometryLabel("handle: LB-02", "LB-02", new LayoutRectangle(52, 203, 67.2, 206.2));
        var identity = new GeometryObjectDisplayIdentity("LB-02", GeometryObjectCategory.LIFTING_BEAM, "LB-02");
        var match = new GeometryLabelMatch(identity, obj, GeometryLabelMatchStatus.Matched, label, new[] { label });
        var audit = GeometryObjectLabelAuditService.Audit(new[] { match }, new[] { label });
        var plan = GeometryObjectLabelLayoutPlanner.Plan(new GeometryObjectLabelPlanningContext(audit, new LayoutRectangle(10, 10, 410, 287), new[] { obj }, new[] { match }, Array.Empty<ExistingGeometryLabel>()));
        Assert.Equal("planned", plan.Status);
        Assert.Single(plan.Moves);
        Assert.Equal(50.4, plan.Moves[0].Dx, 3);
        Assert.Equal(13.5, plan.Moves[0].Dy, 3);
        Assert.Equal(GeometryLabelDirection.Above, plan.Items.Single(x => x.DisplayId == "LB-02").Direction);
    }

    [Fact]
    public void DuplicateRuntimeObjectIdIsRejected() => Assert.Throws<ArgumentException>(() => GeometryObjectDisplayIdAssigner.Assign(new[] { Obj("X", GeometryObjectCategory.LIFTING_BEAM, 1, 1), Obj("X", GeometryObjectCategory.LIFTING_LUG, 2, 2) }));
}
