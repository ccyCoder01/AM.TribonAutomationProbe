using AM.TribonAutomationProbe.Adapter.FileBridge;
using AM.TribonAutomationProbe.Core;
using System.Text.Json;
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
        Assert.Equal(
            "F2B14D4200E1AC239FBF1CFD28D2F99439E631EC2D6FA129ECB6A92A841B75F2",
            attachedFirst.PlanHash);
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
    public void RawVitessePlanHashMustBeValidAndMatchRecomputedValue()
    {
        var value = GeometryLabelPlanBinding.Attach(
            Preflight(
            [
                Item("label:LB-01", "OBJ-1", "LB-01", "LB-01", "READY_TO_CREATE")
            ]));

        GeometryLabelPlanBinding.ValidateRawPlanHash(value);

        var lowercase = value with
        {
            PlanHash = value.PlanHash.ToLowerInvariant()
        };
        var lowercaseError = Assert.Throws<ProbeException>(
            () => GeometryLabelPlanBinding.ValidateRawPlanHash(lowercase));
        Assert.Equal(
            "GEOMETRY_LABEL_PLAN_HASH_MISMATCH",
            lowercaseError.Code);

        var malformed = value with { PlanHash = "ABC" };
        var malformedError = Assert.Throws<ProbeException>(
            () => GeometryLabelPlanBinding.ValidateRawPlanHash(malformed));
        Assert.Equal("GEOMETRY_LABEL_PLAN_HASH_MISMATCH", malformedError.Code);

        var mismatched = value with { PlanHash = new string('A', 64) };
        var mismatchError = Assert.Throws<ProbeException>(
            () => GeometryLabelPlanBinding.ValidateRawPlanHash(mismatched));
        Assert.Equal("GEOMETRY_LABEL_PLAN_HASH_MISMATCH", mismatchError.Code);
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
    public async Task FileBridgeApplyCreatesBoundRequest()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "round43a2-" + Guid.NewGuid().ToString("N"));

        try
        {
            var adapter = new FileBridgeGeometryAutomationAdapter(
                new FileBridgeTransport(
                    new FileBridgeOptions(
                        root,
                        PollIntervalMs: 10,
                        DefaultTimeoutMs: 5000)));

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

            using var cancellation =
                new CancellationTokenSource();

            var task = adapter.ApplyMissingLabelsAsync(
                request,
                cancellation.Token);

            var requestPath = await WaitForRequestAsync(
                root);

            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(requestPath));

            var command = document.RootElement;

            Assert.Equal(
                "geometry.label-apply-missing",
                command.GetProperty("action").GetString());

            var payload = command.GetProperty("payload");

            Assert.True(
                payload.GetProperty("allowWrite").GetBoolean());
            Assert.True(
                payload.GetProperty("writeConfirmed").GetBoolean());
            Assert.Equal(
                "PREFLIGHT-1",
                payload
                    .GetProperty(
                        "confirmedPreflightOperationId")
                    .GetString());
            Assert.Equal(
                request.ConfirmedPlanHash,
                payload
                    .GetProperty("confirmedPlanHash")
                    .GetString());
            Assert.Equal(
                "label:LB-01",
                payload
                    .GetProperty("confirmedOperationIds")
                    .EnumerateArray()
                    .Single()
                    .GetString());

            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<
                OperationCanceledException>(
                async () => await task);
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

    private static async Task<string> WaitForRequestAsync(
        string root)
    {
        var inbox = Path.Combine(root, "inbox");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (Directory.Exists(inbox))
            {
                var path = Directory
                    .EnumerateFiles(
                        inbox,
                        "*.request.json")
                    .SingleOrDefault();

                if (path is not null)
                {
                    return path;
                }
            }

            await Task.Delay(10);
        }

        throw new TimeoutException(
            "Bridge request was not created.");
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
