using System.Text.Json;
using AM.TribonAutomationProbe.Core;
using Xunit;

namespace AM.TribonAutomationProbe.Tests;

public sealed class GeometryCorePocClosureTests
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "GeometryObjectPoc");
    private static T Read<T>(string name) => JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(Fixture, name)), JsonDefaults.Options)!;
    private static (GeometryObjectDetectionResponse Detection, GeometryObjectDisplayIdMap Ids, GeometryLabelInspectionResponse Labels) Load(string labels)
    {
        var detection = Read<GeometryObjectDetectionResponse>("geometry-detection.formal.json");
        var ids = GeometryObjectDisplayIdAssigner.Assign(detection.Objects);
        var response = Read<GeometryLabelInspectionResponse>(labels);
        return (detection, ids, response);
    }

    [Fact]
    public void FormalBeforeAndAfterCloseTheCorePoc()
    {
        var before = Load("geometry-labels-before.formal.json"); GeometryContractValidator.ValidateDetectionResponse(before.Detection); GeometryContractValidator.ValidateDisplayIdMap(before.Ids, before.Detection.Objects); GeometryContractValidator.ValidateLabelInspectionResponse(before.Labels); var beforeMatch = GeometryObjectLabelMatcher.MatchResult(before.Detection.Objects, before.Ids, before.Labels.Labels); GeometryContractValidator.ValidateMatchResult(beforeMatch); var beforeAudit = GeometryObjectLabelAuditService.Audit(beforeMatch, before.Detection.Objects); GeometryContractValidator.ValidateAuditResult(beforeAudit); var beforePlan = GeometryObjectLabelLayoutPlanner.Plan(new GeometryObjectLabelPlanningContext(beforeAudit, before.Detection.DrawingExtent, before.Detection.Objects, beforeMatch.Matches, beforeMatch.OtherTexts)); GeometryContractValidator.ValidatePlan(beforePlan);
        Assert.Equal(12, beforeMatch.Matches.Count); Assert.Equal(1, beforeAudit.OwnObjectOverlapCount); Assert.Equal(1, beforeAudit.NeedsRelayoutCount); Assert.Equal(11, beforePlan.KeepCount); Assert.Equal(1, beforePlan.MoveCount); Assert.Equal("planned", beforePlan.Status); Assert.Single(beforePlan.Moves); Assert.Equal("handle: 65595", beforePlan.Moves[0].RuntimeHandle); Assert.Equal(50.400001526, beforePlan.Moves[0].Dx, 3); Assert.Equal(-44.199996948, beforePlan.Moves[0].Dy, 3);
        var after = Load("geometry-labels-after.formal.json"); var afterMatch = GeometryObjectLabelMatcher.MatchResult(after.Detection.Objects, after.Ids, after.Labels.Labels); var afterAudit = GeometryObjectLabelAuditService.Audit(afterMatch, after.Detection.Objects); var afterPlan = GeometryObjectLabelLayoutPlanner.Plan(new GeometryObjectLabelPlanningContext(afterAudit, after.Detection.DrawingExtent, after.Detection.Objects, afterMatch.Matches, afterMatch.OtherTexts)); GeometryContractValidator.ValidatePlan(afterPlan);
        Assert.Equal(12, afterMatch.Matches.Count); Assert.Equal(0, afterAudit.OwnObjectOverlapCount); Assert.Equal(0, afterAudit.NeedsRelayoutCount); Assert.Equal(12, afterPlan.KeepCount); Assert.Empty(afterPlan.Moves); Assert.Equal("no_changes_required", afterPlan.Status);
    }

    [Fact] public void ReversingInputCollectionsPreservesPocPlan() { var d=Read<GeometryObjectDetectionResponse>("geometry-detection.formal.json"); var ids=GeometryObjectDisplayIdAssigner.Assign(d.Objects.Reverse()); var l=Read<GeometryLabelInspectionResponse>("geometry-labels-before.formal.json"); var m=GeometryObjectLabelMatcher.MatchResult(d.Objects.Reverse().ToArray(),ids,l.Labels.Reverse().ToArray()); var a=GeometryObjectLabelAuditService.Audit(m,d.Objects); var p=GeometryObjectLabelLayoutPlanner.Plan(new GeometryObjectLabelPlanningContext(a,d.DrawingExtent,d.Objects,m.Matches,m.OtherTexts)); Assert.Equal("handle: 65595",p.Moves.Single().RuntimeHandle); Assert.Equal(11,p.KeepCount); }
}
