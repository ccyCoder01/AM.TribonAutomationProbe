using AM.TribonAutomationProbe.Adapter.FileBridge;
using AM.TribonAutomationProbe.Core;
using Xunit;

namespace AM.TribonAutomationProbe.Tests;

public sealed class GeometryLabelPlanBindingTests
{
    [Fact]
    public void AttachProducesStableHashAndSortedReadyOperations()
    {
        var first = Preflight(
        [
            Item(
                "label:PF-02",
                "OBJ-2",
                "PF-02",
                "PF-02",
                "READY_TO_CREATE"),
            Item(
                "label:LB-01",
                "OBJ-1",
                "LB-01",
                "LB-01",
                "READY_TO_CREATE")
        ]);

        var second = Preflight(
        [
            first.Items[1],
            first.Items[0]
        ]);

        var attachedFirst = GeometryLabelPlanBinding.Attach(first);
        var attachedSecond = GeometryLabelPlanBinding.Attach(second);

        Assert.Equal(attachedFirst.PlanHash, attachedSecond.PlanHash);
        Assert.Equal(64, attachedFirst.PlanHash.Length);
        Assert.Equal(
            new[] { "label:LB-01", "label:PF-02" },
            attachedFirst.ReadyOperationIds);
    }

    [Fact]
    public void PlanHashChangesWhenDecisionChanges()
    {
        var original = GeometryLabelPlanBinding.Attach(
            Preflight(
            [
                Item(
                    "label:LB-01",
                    "OBJ-1",
                    "LB-01",
                    "LB-01",
                    "READY_TO_CREATE")
            ]));

        var changed = GeometryLabelPlanBinding.Attach(
            Preflight(
            [
                Item(
                    "label:LB-01",
                    "OBJ-1",
                    "LB-01",
                    "LB-01",
                    "ALREADY_APPLIED",
                    matchCount: 1,
                    matchHandle: "H-1")
            ],
            alreadyPresent: 1,
            missing: 0));

        Assert.NotEqual(original.PlanHash, changed.PlanHash);
    }

    [Fact]
    public void AuthorizationRequiresAllConfirmationFields()
    {
        var error = Assert.Throws<ProbeException>(
            () => GeometryLabelPlanBinding.ValidateAuthorization(
                new GeometryLabelApplyMissingRequest(
                    AllowWrite: true,
                    WriteConfirmed: true)));

        Assert.Equal(
            ProbeErrorCodes.InvalidMessage,
            error.Code);
    }

    [Fact]
    public void ValidationRejectsCandidateSetDrift()
    {
        var current = GeometryLabelPlanBinding.Attach(
            Preflight(
            [
                Item(
                    "label:LB-01",
                    "OBJ-1",
                    "LB-01",
                    "LB-01",
                    "READY_TO_CREATE")
            ]));

        var request = new GeometryLabelApplyMissingRequest(
            OperationId: "APPLY-1",
            AllowWrite: true,
            WriteConfirmed: true,
            ConfirmedPreflightOperationId:
                current.OperationId,
            ConfirmedPlanHash:
                current.PlanHash,
            ConfirmedOperationIds:
            [
                "label:PF-01"
            ]);

        var error = Assert.Throws<ProbeException>(
            () => GeometryLabelPlanBinding.ValidateAgainstPreflight(
                request,
                current));

        Assert.Equal(
            ProbeErrorCodes.VerificationFailed,
            error.Code);
        Assert.Equal(
            "safety",
            error.Category);
    }

    [Fact]
    public async Task FileBridgeApplyFailsClosedBeforeCreatingRequest()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "round43a1-" + Guid.NewGuid().ToString("N"));

        try
        {
            var adapter = new FileBridgeGeometryAutomationAdapter(
                new FileBridgeTransport(
                    new FileBridgeOptions(
                        root,
                        PollIntervalMs: 10,
                        DefaultTimeoutMs: 100)));

            var request = new GeometryLabelApplyMissingRequest(
                OperationId: "APPLY-1",
                AllowWrite: true,
                WriteConfirmed: true,
                ConfirmedPreflightOperationId: "PREFLIGHT-1",
                ConfirmedPlanHash:
                    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                ConfirmedOperationIds:
                [
                    "label:LB-01"
                ]);

            var error = await Assert.ThrowsAsync<ProbeException>(
                () => adapter.ApplyMissingLabelsAsync(
                    request,
                    CancellationToken.None));

            Assert.Equal(
                "GEOMETRY_LABEL_PLAN_BINDING_NOT_ENFORCED",
                error.Code);
            Assert.Equal(
                "safety",
                error.Category);

            var inbox = Path.Combine(root, "inbox");

            Assert.False(
                Directory.Exists(inbox) &&
                Directory.EnumerateFiles(
                    inbox,
                    "*.request.json").Any());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(
                    root,
                    recursive: true);
            }
        }
    }

    [Fact]
    public void ValidationAcceptsExactConfirmedPreflight()
    {
        var current = GeometryLabelPlanBinding.Attach(
            Preflight(
            [
                Item(
                    "label:LB-01",
                    "OBJ-1",
                    "LB-01",
                    "LB-01",
                    "READY_TO_CREATE")
            ]));

        var request = new GeometryLabelApplyMissingRequest(
            OperationId: "APPLY-1",
            AllowWrite: true,
            WriteConfirmed: true,
            ConfirmedPreflightOperationId:
                current.OperationId,
            ConfirmedPlanHash:
                current.PlanHash.ToLowerInvariant(),
            ConfirmedOperationIds:
                current.ReadyOperationIds);

        GeometryLabelPlanBinding.ValidateAgainstPreflight(
            request,
            current);
    }

    private static GeometryLabelPreflightResult Preflight(
        IReadOnlyList<GeometryLabelPreflightItem> items,
        int alreadyPresent = 0,
        int? missing = null) =>
        new(
            SchemaVersion: "1.0",
            TaskType: "geometry.label-preflight",
            OperationId: "PREFLIGHT-1",
            DrawingContext: "current_drafting_context",
            StartedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: DateTimeOffset.UnixEpoch,
            Status: "SUCCESS",
            PreAlreadyPresentCount: alreadyPresent,
            PreMissingCount:
                missing ??
                items.Count(
                    x => x.Decision == "READY_TO_CREATE"),
            PreDuplicateTextCount: 0,
            PreInspectionErrorCount: 0,
            Items: items,
            DrawingWritePerformed: false,
            SavePerformed: false,
            PreTextConflictCount: 0);

    private static GeometryLabelPreflightItem Item(
        string operationId,
        string sourceObjectId,
        string stableObjectId,
        string expectedText,
        string decision,
        int matchCount = 0,
        string? matchHandle = null) =>
        new(
            OperationId: operationId,
            SourceObjectId: sourceObjectId,
            StableObjectId: stableObjectId,
            ExpectedText: expectedText,
            MatchCount: matchCount,
            NearestDistance: 0,
            AllowedDistance: 12,
            Decision: decision,
            MatchHandle: matchHandle);
}
