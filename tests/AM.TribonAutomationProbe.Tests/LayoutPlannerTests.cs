using AM.TribonAutomationProbe.Core;
using Xunit;

namespace AM.TribonAutomationProbe.Tests;

public sealed class LayoutPlannerTests
{
    private static AnnotationLayoutSnapshot Snapshot(params AnnotationLayoutItem[] items) => new() { DrawingExtent = new(0, 0, 100, 100), Items = items };
    private static AnnotationLayoutItem Item(string role, string type, string handle, LayoutRectangle label, LayoutRectangle? parent = null, string? text = null) => new() { Role = role, Type = type, RuntimeHandle = handle, LabelExtent = label, ParentExtent = parent ?? label, Text = text };

    [Fact] public void OverlapIsConflictAndExactClearanceIsSeparate()
    {
        var overlap = AnnotationLayoutPlanner.Plan(Snapshot(Item("movable", "position_number", "A", new(1, 1, 3, 3)), Item("obstacle", "text", "B", new(1, 1, 3, 3))));
        Assert.Equal(1, overlap.InitialConflictCount);
        var edge = AnnotationLayoutPlanner.Plan(Snapshot(Item("movable", "position_number", "A", new(1, 1, 2, 2)), Item("obstacle", "text", "B", new(2.5, 1, 3.5, 2))));
        Assert.Equal(0, edge.InitialConflictCount);
    }

    [Fact] public void ParentOverlapDoesNotMatterWhenLabelsAreSeparate() => Assert.Equal(0, AnnotationLayoutPlanner.Plan(Snapshot(Item("movable", "position_number", "A", new(1, 1, 2, 2), new(0, 0, 10, 10)), Item("obstacle", "text", "B", new(20, 20, 21, 21)))).InitialConflictCount);
    [Fact] public void ObstacleNeverMoves() { var plan = AnnotationLayoutPlanner.Plan(Snapshot(Item("obstacle", "text", "B", new(1, 1, 3, 3)), Item("movable", "position_number", "A", new(1, 1, 3, 3)))); Assert.All(plan.Moves, x => Assert.NotEqual("B", x.RuntimeHandle)); }
    [Fact] public void CandidateMustStayInsideDrawing() { var plan = AnnotationLayoutPlanner.Plan(new() { DrawingExtent = new(0, 0, 3, 3), Items = new[] { Item("movable", "position_number", "A", new(0, 0, 2, 2)), Item("obstacle", "text", "B", new(0, 0, 2, 2)) } }); Assert.Equal("no_solution", plan.Status); }
    [Fact] public void N4CaseChoosesZeroMinusThree() { LayoutRectangle s1 = new(24.7831039429, 132.348770142, 29.1581039429, 134.848770142); var plan = AnnotationLayoutPlanner.Plan(new() { DrawingExtent = new(0, 0, 287, 200), Items = new[] { Item("movable", "position_number", "N4", s1, s1, "N4"), Item("obstacle", "position_number", "S1", s1, s1, "S1") } }); Assert.Contains(plan.Moves, x => x.RuntimeHandle == "N4" && x.Dx == 0 && x.Dy == -3); Assert.Equal(0, plan.RemainingConflictCount); }
    [Fact] public void NoConflictNeedsNoChanges() { var plan = AnnotationLayoutPlanner.Plan(Snapshot(Item("movable", "position_number", "A", new(1, 1, 2, 2)), Item("obstacle", "text", "B", new(10, 10, 11, 11)))); Assert.Equal("no_changes_required", plan.Status); Assert.Empty(plan.Moves); }
}
