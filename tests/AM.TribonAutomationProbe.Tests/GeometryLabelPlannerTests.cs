using System.Text.Json;
using AM.TribonAutomationProbe.Core;
using Xunit;

namespace AM.TribonAutomationProbe.Tests;
public sealed class GeometryLabelPlannerTests
{
    private static T Read<T>(string n) => JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","GeometryObjectPoc",n)),JsonDefaults.Options)!;
    private static GeometryObjectLabelPlan Plan(string n) { var d=Read<GeometryObjectDetectionResponse>("geometry-detection.formal.json"); var ids=GeometryObjectDisplayIdAssigner.Assign(d.Objects); var l=Read<GeometryLabelInspectionResponse>(n); var m=GeometryObjectLabelMatcher.MatchResult(d.Objects,ids,l.Labels); var a=GeometryObjectLabelAuditService.Audit(m,d.Objects); return GeometryObjectLabelLayoutPlanner.Plan(new GeometryObjectLabelPlanningContext(a,d.DrawingExtent,d.Objects,m.Matches,m.OtherTexts)); }
    [Fact] public void FormalBeforeAndAfterUseCompletePlanner() { var b=Plan("geometry-labels-before.formal.json"); Assert.Equal("planned",b.Status); Assert.Equal(1,b.InitialConflictCount); Assert.Equal(11,b.KeepCount); Assert.Single(b.Moves); Assert.Equal(GeometryLabelDirection.Below,b.Items.Single(x=>x.DisplayId=="LB-02").Direction); var a=Plan("geometry-labels-after.formal.json"); Assert.Equal("no_changes_required",a.Status); Assert.Equal(12,a.KeepCount); Assert.Empty(a.Moves); }
    [Fact] public void LegacyRelayoutOverloadCannotCreateUnsafePlan() { var a=Plan("geometry-labels-before.formal.json"); var ex=Assert.Throws<ProbeException>(()=>GeometryObjectLabelLayoutPlanner.Plan(new GeometryObjectLabelAuditResult("1.0","succeeded",1,1,0,0,1,0,0,0,1,a.Items.Where(x=>x.DisplayId=="LB-02").Select(x=>new GeometryLabelAuditItem(x.OperationId,x.RuntimeObjectId,x.DisplayId,x.Category,GeometryLabelMatchStatus.Matched,x.RuntimeLabelHandle,x.ExpectedText,new(0,0,1,1),x.CurrentExtent,GeometryLabelDirection.Overlapping,1,0,Array.Empty<string>(),Array.Empty<string>(),Array.Empty<string>(),"needs_relayout")).ToArray()),new(0,0,10,10))); Assert.Equal(ProbeErrorCodes.InvalidMessage,ex.Code); }
    [Fact] public void PlannerKeepsClearLabelsAndUsesAtomicNoSolutionContract() { var a=Plan("geometry-labels-after.formal.json"); Assert.All(a.Items,x=>Assert.Equal(GeometryLabelPlanAction.KeepCurrent,x.Action)); Assert.Empty(a.Moves); Assert.False(a.RequiresAllowWrite); }
}
