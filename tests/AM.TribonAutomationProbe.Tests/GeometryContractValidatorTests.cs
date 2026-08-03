using System.Text.Json;
using AM.TribonAutomationProbe.Core;
using Xunit;

namespace AM.TribonAutomationProbe.Tests;
public sealed class GeometryContractValidatorTests
{
    private static string PathFor(string n) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "GeometryObjectPoc", n);
    [Fact] public void FormalDetectionAndLabelsPassContractValidation() { var d = JsonSerializer.Deserialize<GeometryObjectDetectionResponse>(File.ReadAllText(PathFor("geometry-detection.formal.json")), JsonDefaults.Options)!; var l = JsonSerializer.Deserialize<GeometryLabelInspectionResponse>(File.ReadAllText(PathFor("geometry-labels-before.formal.json")), JsonDefaults.Options)!; GeometryContractValidator.ValidateDetectionResponse(d); GeometryContractValidator.ValidateLabelInspectionResponse(l); }
    [Fact] public void InvalidDetectionRectangleIsStructuredValidationFailure() { var d = new GeometryObjectDetectionResponse("1.0", "r", "succeeded", "scope", new(double.NaN, 0, 1, 1), Array.Empty<DetectedGeometryObject>(), new()); var ex = Assert.Throws<ProbeException>(() => GeometryContractValidator.ValidateDetectionResponse(d)); Assert.Equal(ProbeErrorCodes.InvalidMessage, ex.Code); Assert.Equal("validation", ex.Category); }
    [Fact] public void InvalidPlanCombinationIsRejected() { var p = new GeometryObjectLabelPlan("1.0", "t", "dry-run", "no_solution", new(0,0,10,10), 1, 1, 0, 0, 0, true, false, Array.Empty<GeometryObjectLabelPlanItem>(), new[] { new GeometryObjectLabelMove("1", "A", "A", new(1,1,2,2), new(2,1,3,2), 1, 0) }); var ex = Assert.Throws<ProbeException>(() => GeometryContractValidator.ValidatePlan(p)); Assert.Equal(ProbeErrorCodes.InvalidMessage, ex.Code); }
    [Fact] public void EveryValidatorRejectsNullStructurally() { Assert.Throws<ProbeException>(()=>GeometryContractValidator.ValidateDetectionResponse(null!)); Assert.Throws<ProbeException>(()=>GeometryContractValidator.ValidateLabelInspectionResponse(null!)); Assert.Throws<ProbeException>(()=>GeometryContractValidator.ValidateDisplayIdMap(null!,Array.Empty<DetectedGeometryObject>())); Assert.Throws<ProbeException>(()=>GeometryContractValidator.ValidateMatchResult(null!)); Assert.Throws<ProbeException>(()=>GeometryContractValidator.ValidateAuditResult(null!)); Assert.Throws<ProbeException>(()=>GeometryContractValidator.ValidatePlan(null!)); }
}
