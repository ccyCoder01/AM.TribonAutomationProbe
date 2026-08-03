namespace AM.TribonAutomationProbe.Core;

public sealed record GeometryLabelPlanContract(
    string OperationId,
    string StableObjectId,
    string ExpectedText,
    double PlannedX,
    double PlannedY,
    double PlannedHeight,
    string PlannedColour,
    LayoutRectangle TargetExtent,
    double AllowedDistance);

public sealed record GeometryObservedLabelContract(
    string RuntimeHandle,
    string Text,
    double X,
    double Y,
    double Height,
    string Colour,
    LayoutRectangle Extent);

public sealed record GeometryLabelPostcheckInput(
    IReadOnlyList<GeometryLabelPlanContract> Plan,
    IReadOnlyList<GeometryObservedLabelContract> Observed,
    IReadOnlySet<string> PreExistingOperationIds,
    IReadOnlySet<string> CreatedOperationIds,
    IReadOnlyList<string> FailedOperationIds,
    int InspectionErrorCount = 0,
    double PositionTolerance = 0.01,
    double HeightTolerance = 0.01);

public sealed record GeometryLabelPostcheckDecision(
    string Status,
    int PostValidLabelCount,
    int PostMissingCount,
    int PostDuplicateCount,
    int PostCreatedValidCount,
    int PostCreatedPropertyErrorCount,
    int PostExistingMatchErrorCount,
    int PostExistingPropertyDriftCount,
    int PostInspectionErrorCount,
    bool ManualRecoveryRequired,
    IReadOnlyList<GeometryLabelPropertyDrift> ExistingPropertyDrifts);

public static class GeometryLabelPostcheckEvaluator
{
    public static GeometryLabelPostcheckDecision Evaluate(GeometryLabelPostcheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var observedByText = input.Observed
            .GroupBy(x => x.Text, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);

        var postValid = 0;
        var postMissing = 0;
        var postDuplicate = 0;
        var createdValid = 0;
        var createdPropertyErrors = 0;
        var existingMatchErrors = 0;
        var drifts = new List<GeometryLabelPropertyDrift>();

        foreach (var plan in input.Plan)
        {
            observedByText.TryGetValue(plan.ExpectedText, out var matches);
            matches ??= Array.Empty<GeometryObservedLabelContract>();

            var isCreated = input.CreatedOperationIds.Contains(plan.OperationId);
            var isExisting = input.PreExistingOperationIds.Contains(plan.OperationId);

            if (matches.Length == 0)
            {
                postMissing++;
                if (isCreated)
                    createdPropertyErrors++;
                else
                    existingMatchErrors++;
                continue;
            }

            if (matches.Length > 1)
            {
                postDuplicate++;
                if (isCreated)
                    createdPropertyErrors++;
                else
                    existingMatchErrors++;
                continue;
            }

            var observed = matches[0];
            var distance = DistanceToExtent(
                observed.Extent.CenterX,
                observed.Extent.CenterY,
                plan.TargetExtent);

            if (isCreated)
            {
                var strictOk =
                    Math.Abs(observed.X - plan.PlannedX) <= input.PositionTolerance &&
                    Math.Abs(observed.Y - plan.PlannedY) <= input.PositionTolerance &&
                    Math.Abs(observed.Height - plan.PlannedHeight) <= input.HeightTolerance &&
                    string.Equals(observed.Colour, plan.PlannedColour, StringComparison.OrdinalIgnoreCase) &&
                    distance <= plan.AllowedDistance;

                if (strictOk)
                {
                    createdValid++;
                    postValid++;
                }
                else
                {
                    createdPropertyErrors++;
                }

                continue;
            }

            if (!isExisting || distance > plan.AllowedDistance)
            {
                existingMatchErrors++;
                continue;
            }

            postValid++;

            var fields = new List<string>();
            if (Math.Abs(observed.X - plan.PlannedX) > input.PositionTolerance)
                fields.Add("X");
            if (Math.Abs(observed.Y - plan.PlannedY) > input.PositionTolerance)
                fields.Add("Y");
            if (Math.Abs(observed.Height - plan.PlannedHeight) > input.HeightTolerance)
                fields.Add("HEIGHT");
            if (!string.Equals(observed.Colour, plan.PlannedColour, StringComparison.OrdinalIgnoreCase))
                fields.Add("COLOUR");

            if (fields.Count > 0)
            {
                drifts.Add(new GeometryLabelPropertyDrift(
                    plan.OperationId,
                    plan.StableObjectId,
                    fields,
                    observed.X,
                    observed.Y,
                    observed.Height,
                    observed.Colour,
                    plan.PlannedX,
                    plan.PlannedY,
                    plan.PlannedHeight,
                    plan.PlannedColour));
            }
        }

        var manualRecoveryRequired = input.FailedOperationIds.Count > 0;

        var status = manualRecoveryRequired
            ? "PARTIAL_FAILURE"
            : postMissing > 0 ||
              postDuplicate > 0 ||
              createdPropertyErrors > 0 ||
              existingMatchErrors > 0 ||
              input.InspectionErrorCount > 0
                ? "FAILED_POSTCHECK"
                : "SUCCESS";

        return new GeometryLabelPostcheckDecision(
            status,
            postValid,
            postMissing,
            postDuplicate,
            createdValid,
            createdPropertyErrors,
            existingMatchErrors,
            drifts.Count,
            input.InspectionErrorCount,
            manualRecoveryRequired,
            drifts);
    }

    private static double DistanceToExtent(double x, double y, LayoutRectangle extent)
    {
        var dx = x < extent.X1
            ? extent.X1 - x
            : x > extent.X2
                ? x - extent.X2
                : 0;

        var dy = y < extent.Y1
            ? extent.Y1 - y
            : y > extent.Y2
                ? y - extent.Y2
                : 0;

        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
