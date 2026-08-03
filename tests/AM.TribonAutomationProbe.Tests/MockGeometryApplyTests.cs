using AM.TribonAutomationProbe.Adapter.Mock;
using AM.TribonAutomationProbe.Core;
using Xunit;

namespace AM.TribonAutomationProbe.Tests;

public sealed class MockGeometryApplyTests
{
    private static LayoutRectangle R(double x, double y) => new(x, y, x + 10, y + 4);
    private static ExistingGeometryLabel Label(string handle, string text, LayoutRectangle extent) => new(handle, text, extent);
    private static GeometryObjectLabelMove Move(string op, string handle, string text, LayoutRectangle before, double dx, double dy) => new(op, handle, text, before, before.Move(dx, dy), dx, dy);
    private static MockTribonGeometryState State(params ExistingGeometryLabel[] labels) { var state = new MockTribonGeometryState(); state.ExistingLabels.AddRange(labels); return state; }
    private static MockTribonGeometryState State(string verify, params ExistingGeometryLabel[] labels) => StateWithBehavior(verify, null, labels);
    private static MockTribonGeometryState State(string verify, string rollback, params ExistingGeometryLabel[] labels) => StateWithBehavior(verify, rollback, labels);
    private static MockTribonGeometryState StateWithBehavior(string? verify, string? rollback, params ExistingGeometryLabel[] labels) { var state = new MockTribonGeometryState { Behavior = new(verify, rollback) }; state.ExistingLabels.AddRange(labels); return state; }
    private static GeometryObjectLabelApplyRequest Request(bool allow, params GeometryObjectLabelMove[] moves) => new(AllowWrite: allow, RequestId: "r", Moves: moves);
    private static async Task<GeometryObjectLabelApplyResponse> Apply(MockTribonGeometryState state, GeometryObjectLabelApplyRequest request) => await new MockTribonAdapter(state).ApplyGeometryLabelMovesAsync(request, CancellationToken.None);
    private static async Task<IReadOnlyList<ExistingGeometryLabel>> InspectAsync(MockTribonAdapter adapter) => (await adapter.InspectGeometryLabelsAsync(new(), CancellationToken.None)).Labels;
    private static void AssertLabels(MockTribonGeometryState state, params ExistingGeometryLabel[] expected) => Assert.Equal(expected, state.ExistingLabels);

    [Fact] public async Task EmptyRequestIsReadOnlyAndSaveFlagsAgree()
    {
        var response = await Apply(State(Label("A", "A", R(0, 0))), Request(false));
        Assert.Empty(response.Receipts); Assert.False(response.Diagnostics.WriteAttempted); Assert.False(response.Diagnostics.NetDrawingChange); Assert.False(response.Diagnostics.RollbackAttempted); Assert.Null(response.Diagnostics.RollbackSucceeded); Assert.False(response.SavePerformed); Assert.False(response.Diagnostics.SavePerformed);
    }

    [Fact] public async Task UnauthorizedRequestAndCancellationDoNotChangeState()
    {
        var state = State(Label("A", "A", R(0, 0))); var before = state.ExistingLabels.ToArray(); var adapter = new MockTribonAdapter(state);
        await Assert.ThrowsAsync<ProbeException>(() => Apply(state, Request(false, Move("1", "A", "A", R(0, 0), 1, 0))));
        Assert.Equal(before, await InspectAsync(adapter));
        using var cts = new CancellationTokenSource(); cts.Cancel(); await Assert.ThrowsAsync<OperationCanceledException>(() => new MockTribonAdapter(state).ApplyGeometryLabelMovesAsync(Request(true, Move("1", "A", "A", R(0, 0), 1, 0)), cts.Token)); Assert.Equal(before, state.ExistingLabels);
    }

    [Fact] public async Task PreflightRejectsIdentityTextAndStateProblemsWithoutWriting()
    {
        var state = State(labels: new[] { Label("A", "A", R(0, 0)), Label("B", "B", R(20, 0)) }); var before = state.ExistingLabels.ToArray();
        var cases = new[] { Request(true, Move("", "A", "A", R(0, 0), 1, 0)), Request(true, Move("1", "", "A", R(0, 0), 1, 0)), Request(true, Move("1", "A", "", R(0, 0), 1, 0)), Request(true, Move("1", "Z", "Z", R(0, 0), 1, 0)), Request(true, Move("1", "A", "wrong", R(0, 0), 1, 0)), Request(true, new GeometryObjectLabelMove("1", "A", "A", new(double.NaN, 0, 1, 1), R(1, 0), 1, 0)), Request(true, new GeometryObjectLabelMove("1", "A", "A", R(0, 0), R(3, 0), 1, 0)) };
        foreach (var request in cases) { var ex = await Assert.ThrowsAsync<ProbeException>(() => Apply(state, request)); Assert.NotEmpty(ex.Code); Assert.NotEmpty(ex.Category); Assert.Equal(before, state.ExistingLabels); }
        var duplicate = State(labels: new[] { Label("A", "A", R(0, 0)), Label("A", "A2", R(20, 0)) }); var duplicateEx = await Assert.ThrowsAsync<ProbeException>(() => Apply(duplicate, Request(true, Move("1", "A", "A", R(0, 0), 1, 0)))); Assert.Equal(ProbeErrorCodes.ObjectStateChanged, duplicateEx.Code); Assert.Equal("concurrency", duplicateEx.Category);
    }

    [Fact] public async Task SecondPreflightFailureLeavesFirstUntouched()
    {
        var state = State(Label("A", "A", R(0, 0)), Label("B", "B", R(20, 0))); var before = state.ExistingLabels.ToArray();
        await Assert.ThrowsAsync<ProbeException>(() => Apply(state, Request(true, Move("1", "A", "A", R(0, 0), 1, 0), Move("2", "B", "wrong", R(20, 0), 1, 0)))); Assert.Equal(before, state.ExistingLabels);
    }

    [Fact] public async Task ApplyIsIdempotentAndReturnsStableOrderedReceipts()
    {
        var state = State(Label("A", "A", R(0, 0)), Label("B", "B", R(20, 0))); var request = Request(true, Move("1", "A", "A", R(0, 0), 1, 0), Move("2", "B", "B", R(20, 0), 2, 0)); var adapter = new MockTribonAdapter(state);
        var first = await adapter.ApplyGeometryLabelMovesAsync(request, CancellationToken.None); Assert.Equal(new[] { "applied", "applied" }, first.Receipts.Select(x => x.Status));
        var second = await adapter.ApplyGeometryLabelMovesAsync(request, CancellationToken.None); Assert.Equal(new[] { "already_applied", "already_applied" }, second.Receipts.Select(x => x.Status)); Assert.False(second.Diagnostics.WriteAttempted); Assert.False(second.Diagnostics.NetDrawingChange);
    }

    [Fact] public async Task VerificationFailureRollsBackEveryChangedItemWithOneReceipt()
    {
        var state = State(Label("A", "A", R(0, 0)), Label("B", "B", R(20, 0))); var request = Request(true, Move("1", "A", "A", R(0, 0), 1, 0), Move("2", "B", "B", R(20, 0), 2, 0));
        var stateWithFailure = State("2", labels: state.ExistingLabels.ToArray()); var response = await Apply(stateWithFailure, request); Assert.Equal("failed_rolled_back", response.Status); Assert.Equal(new[] { R(0, 0), R(20, 0) }, stateWithFailure.ExistingLabels.Select(x => x.Extent)); Assert.True(response.Diagnostics.RollbackSucceeded); Assert.False(response.Diagnostics.NetDrawingChange); Assert.Equal(new[] { "rolled_back", "rolled_back" }, response.Receipts.Select(x => x.Status)); Assert.Equal(2, response.Receipts.Select(x => x.OperationId).Distinct().Count());
    }

    [Fact] public async Task RollbackFailureKeepsFailedItemAndContinuesOtherRollback()
    {
        var state = State(Label("A", "A", R(0, 0)), Label("B", "B", R(20, 0))); var request = Request(true, Move("1", "A", "A", R(0, 0), 1, 0), Move("2", "B", "B", R(20, 0), 2, 0));
        var stateWithFailure = State("2", "1", state.ExistingLabels.ToArray()); var response = await Apply(stateWithFailure, request); Assert.Equal("failed_rollback", response.Status); Assert.Equal(new[] { R(1, 0), R(20, 0) }, stateWithFailure.ExistingLabels.Select(x => x.Extent)); Assert.False(response.Diagnostics.RollbackSucceeded); Assert.True(response.Diagnostics.NetDrawingChange); Assert.Equal(new[] { "rollback_failed", "rolled_back" }, response.Receipts.Select(x => x.Status));
    }

    [Fact] public async Task AlreadyAppliedItemIsNotRolledBackAndPendingAfterFailureIsNotAttempted()
    {
        var state = State(Label("A", "A", R(1, 0)), Label("B", "B", R(20, 0)), Label("C", "C", R(40, 0))); var request = Request(true, Move("1", "A", "A", R(0, 0), 1, 0), Move("2", "B", "B", R(20, 0), 2, 0), Move("3", "C", "C", R(40, 0), 3, 0));
        var stateWithFailure = State("2", labels: state.ExistingLabels.ToArray()); var response = await Apply(stateWithFailure, request); Assert.Equal(new[] { "already_applied", "rolled_back", "not_attempted" }, response.Receipts.Select(x => x.Status)); Assert.False(response.Diagnostics.NetDrawingChange); Assert.Equal(new[] { R(1, 0), R(20, 0), R(40, 0) }, stateWithFailure.ExistingLabels.Select(x => x.Extent));
    }

    [Fact] public async Task DetectInspectAndCloneReturnIndependentCollections()
    {
        var objectValue = new DetectedGeometryObject("O", GeometryObjectCategory.LIFTING_BEAM, "high", R(0, 0), new[] { "s" }, new[] { "g" }, 1, new(1)); var state = new MockTribonGeometryState { DetectedObjects = new[] { objectValue } }; state.ExistingLabels.Add(Label("A", "A", R(0, 0))); var adapter = new MockTribonAdapter(state);
        var detected = await adapter.DetectGeometryObjectsAsync(new(), CancellationToken.None); ((string[])detected.Objects[0].SeedHandles)[0] = "changed"; ((string[])detected.Objects[0].GeometryHandles)[0] = "changed"; var detectedAgain = await adapter.DetectGeometryObjectsAsync(new(), CancellationToken.None); Assert.Equal("s", detectedAgain.Objects[0].SeedHandles[0]); Assert.Equal("g", detectedAgain.Objects[0].GeometryHandles[0]); var clone = state.Clone(); clone.ExistingLabels[0] = Label("A", "changed", R(9, 9)); ((string[])clone.DetectedObjects[0].SeedHandles)[0] = "clone"; Assert.Equal("A", state.ExistingLabels[0].Text); Assert.Equal("s", state.DetectedObjects[0].SeedHandles[0]);
        var inspected = await adapter.InspectGeometryLabelsAsync(new(), CancellationToken.None); Assert.NotSame(state.ExistingLabels, inspected.Labels);
    }

    [Fact] public async Task InspectCannotObservePartialApplyState()
    {
        using var entered = new ManualResetEventSlim(false); using var release = new ManualResetEventSlim(false);
        var state = new MockTribonGeometryState { Behavior = new(null, null, entered, release) }; state.ExistingLabels.Add(Label("A", "A", R(0, 0))); state.ExistingLabels.Add(Label("B", "B", R(20, 0))); var adapter = new MockTribonAdapter(state);
        var applyTask = Task.Run(() => adapter.ApplyGeometryLabelMovesAsync(Request(true, Move("1", "A", "A", R(0, 0), 1, 0), Move("2", "B", "B", R(20, 0), 2, 0)), CancellationToken.None));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2))); var inspectTask = Task.Run(() => adapter.InspectGeometryLabelsAsync(new(), CancellationToken.None)); await Task.Yield(); Assert.False(inspectTask.IsCompleted); release.Set(); await applyTask; var labels = (await inspectTask).Labels; Assert.Equal(new[] { R(1, 0), R(22, 0) }, labels.Select(x => x.Extent));
    }
}
