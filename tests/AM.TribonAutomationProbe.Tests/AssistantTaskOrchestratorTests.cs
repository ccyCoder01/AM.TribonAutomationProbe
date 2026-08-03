using AM.TribonAutomationProbe.Core;
using Xunit;

namespace AM.TribonAutomationProbe.Tests;

public sealed class AssistantTaskOrchestratorTests
{
    [Fact]
    public async Task ReadOnlyTaskExecutesWithoutWriteConfirmation()
    {
        var adapter = new FakeGeometryAutomationAdapter();
        var result = await Orchestrator(adapter).RunAsync(
            new AssistantConversationContext("识别当前图纸中的目标对象"),
            new AssistantExecutionAuthorization(),
            cancellationToken: CancellationToken.None);

        Assert.Equal(AssistantTaskState.Completed, result.State);
        Assert.Equal(1, adapter.DetectCallCount);
        Assert.Single(result.TaskResults);
        Assert.False(result.TaskResults[0].DrawingWritePerformed);
        Assert.False(result.TaskResults[0].SavePerformed);
        AssertProgressOrder(
            result,
            AssistantTaskState.Received,
            AssistantTaskState.Interpreting,
            AssistantTaskState.Planned,
            AssistantTaskState.Queued,
            AssistantTaskState.WaitingForTribon,
            AssistantTaskState.Executing,
            AssistantTaskState.Verifying,
            AssistantTaskState.Completed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task WriteTaskDoesNotExecuteUntilConfirmedAndAuthorized(
        bool allowWrite,
        bool writeConfirmed)
    {
        var adapter = new FakeGeometryAutomationAdapter();
        var result = await Orchestrator(adapter).RunAsync(
            new AssistantConversationContext("创建缺失的对象标签"),
            new AssistantExecutionAuthorization(allowWrite, writeConfirmed),
            cancellationToken: CancellationToken.None);

        Assert.Equal(AssistantTaskState.AwaitingConfirmation, result.State);
        Assert.Equal(0, adapter.ApplyCallCount);
        Assert.Empty(result.TaskResults);
        Assert.Contains("缺少", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmedWriteTaskExecutesWithoutSaving()
    {
        var adapter = new FakeGeometryAutomationAdapter();
        var result = await Orchestrator(adapter).RunAsync(
            new AssistantConversationContext("创建缺失的对象标签"),
            new AssistantExecutionAuthorization(
                AllowWrite: true,
                WriteConfirmed: true),
            cancellationToken: CancellationToken.None);

        Assert.Equal(AssistantTaskState.Completed, result.State);
        Assert.Equal(1, adapter.ApplyCallCount);
        var task = Assert.Single(result.TaskResults);
        Assert.True(task.DrawingWritePerformed);
        Assert.False(task.SavePerformed);
        Assert.Contains("创建 12 个", task.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadOnlyTaskReportingDrawingWriteFailsSafetyVerification()
    {
        var adapter = new FakeGeometryAutomationAdapter
        {
            DetectResultFactory = operationId =>
                FakeGeometryAutomationAdapter.Detection(
                    operationId,
                    drawingWritePerformed: true)
        };

        var result = await Orchestrator(adapter).RunAsync(
            new AssistantConversationContext("识别当前图纸中的目标对象"),
            new AssistantExecutionAuthorization(),
            cancellationToken: CancellationToken.None);

        Assert.Equal(AssistantTaskState.Failed, result.State);
        Assert.NotNull(result.Error);
        Assert.Equal(ProbeErrorCodes.VerificationFailed, result.Error!.Code);
        Assert.Equal("safety", result.Error!.Category);
    }

    [Fact]
    public async Task UnexpectedSaveFailsSafetyVerification()
    {
        var adapter = new FakeGeometryAutomationAdapter
        {
            ApplyResultFactory = operationId =>
                FakeGeometryAutomationAdapter.Apply(
                    operationId,
                    savePerformed: true)
        };

        var result = await Orchestrator(adapter).RunAsync(
            new AssistantConversationContext("创建缺失的对象标签"),
            new AssistantExecutionAuthorization(true, true),
            cancellationToken: CancellationToken.None);

        Assert.Equal(AssistantTaskState.Failed, result.State);
        Assert.NotNull(result.Error);
        Assert.Equal(ProbeErrorCodes.SaveFailed, result.Error!.Code);
        Assert.Equal("safety", result.Error!.Category);
    }

    [Fact]
    public async Task TransportFailureIsConvertedToAssistantError()
    {
        var adapter = new FakeGeometryAutomationAdapter
        {
            DetectException = new ProbeException(
                ProbeErrorCodes.CommandTimeout,
                "Timed out waiting for Tribon",
                "timeout",
                true)
        };

        var result = await Orchestrator(adapter).RunAsync(
            new AssistantConversationContext("识别当前图纸中的目标对象"),
            new AssistantExecutionAuthorization(),
            cancellationToken: CancellationToken.None);

        Assert.Equal(AssistantTaskState.Failed, result.State);
        Assert.NotNull(result.Error);
        Assert.Equal(ProbeErrorCodes.CommandTimeout, result.Error!.Code);
        Assert.True(result.Error!.Retryable);
        Assert.Contains("Timed out", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompoundInstructionExecutesInPlanOrder()
    {
        var adapter = new FakeGeometryAutomationAdapter();
        var result = await Orchestrator(adapter).RunAsync(
            new AssistantConversationContext(
                "先看看图纸里有多少吊梁、吊耳和法兰，然后把所有法兰高亮出来"),
            new AssistantExecutionAuthorization(),
            cancellationToken: CancellationToken.None);

        Assert.Equal(AssistantTaskState.Completed, result.State);
        Assert.Equal(2, result.TaskResults.Count);
        Assert.Equal("geometry.detect", result.TaskResults[0].TaskType);
        Assert.Equal("geometry.highlight-flanges", result.TaskResults[1].TaskType);
        Assert.Equal(1, adapter.DetectCallCount);
        Assert.Equal(1, adapter.HighlightCallCount);
    }

    private static AssistantTaskOrchestrator Orchestrator(
        IGeometryAutomationAdapter adapter) =>
        new(
            new RuleBasedAssistantLanguageModel(),
            new AssistantTaskPlanner(),
            adapter,
            new AssistantResultFormatter());

    private static void AssertProgressOrder(
        AssistantRunResult result,
        params AssistantTaskState[] expected)
    {
        Assert.Equal(expected, result.Progress.Select(x => x.State).ToArray());
    }

    private sealed class FakeGeometryAutomationAdapter : IGeometryAutomationAdapter
    {
        public int DetectCallCount { get; private set; }
        public int HighlightCallCount { get; private set; }
        public int ClearCallCount { get; private set; }
        public int PreflightCallCount { get; private set; }
        public int ApplyCallCount { get; private set; }

        public ProbeException? DetectException { get; init; }
        public Func<string, GeometryDetectionResult> DetectResultFactory { get; init; } =
            operationId => Detection(operationId);
        public Func<string, GeometryLabelApplyMissingResult> ApplyResultFactory { get; init; } =
            operationId => Apply(operationId);

        public Task<GeometryDetectionResult> DetectAsync(
            GeometryDetectionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DetectCallCount++;

            var detectException = DetectException;

            if (detectException is not null)
            {
                throw detectException;
            }

            return Task.FromResult(DetectResultFactory(request.OperationId));
        }

        public Task<GeometryHighlightResult> HighlightAsync(
            GeometryHighlightRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HighlightCallCount++;
            var flanges = request.TaskType == "geometry.highlight-flanges";

            return Task.FromResult(
                new GeometryHighlightResult(
                    "1.0",
                    request.TaskType,
                    request.OperationId,
                    "current_drafting_context",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    "succeeded",
                    false,
                    flanges ? 7 : 5,
                    flanges ? 71 : 42,
                    flanges ? 71 : 42,
                    0,
                    0,
                    request.Categories ?? Array.Empty<GeometryObjectCategory>(),
                    false));
        }

        public Task<GeometryHighlightClearResult> ClearHighlightAsync(
            GeometryHighlightClearRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClearCallCount++;

            return Task.FromResult(
                new GeometryHighlightClearResult(
                    "1.0",
                    request.TaskType,
                    request.OperationId,
                    "current_drafting_context",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    "succeeded",
                    true,
                    false,
                    false));
        }

        public Task<GeometryLabelPreflightResult> PreflightLabelsAsync(
            GeometryLabelPreflightRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PreflightCallCount++;

            return Task.FromResult(
                new GeometryLabelPreflightResult(
                    "1.0",
                    request.TaskType,
                    request.OperationId,
                    "current_drafting_context",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    "SUCCESS",
                    12,
                    0,
                    0,
                    0,
                    Array.Empty<GeometryLabelPreflightItem>(),
                    false,
                    false,
                    0));
        }

        public Task<GeometryLabelApplyMissingResult> ApplyMissingLabelsAsync(
            GeometryLabelApplyMissingRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyCallCount++;

            if (!request.AllowWrite)
            {
                throw new ProbeException(
                    ProbeErrorCodes.InvalidMessage,
                    "allowWrite must be true",
                    "validation");
            }

            return Task.FromResult(ApplyResultFactory(request.OperationId));
        }

        public static GeometryDetectionResult Detection(
            string operationId,
            bool drawingWritePerformed = false) =>
            new(
                "1.0",
                "geometry.detect",
                operationId,
                "current_drafting_context",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                "succeeded",
                drawingWritePerformed,
                Array.Empty<DetectedGeometryObject>(),
                new GeometryObjectDetectionDiagnostics(
                    CapturedContourCount: 135,
                    AssignedUniqueContourCount: 113,
                    UnassignedContourCount: 22,
                    ConflictHandleCount: 0,
                    ParseFailureCount: 0),
                false);

        public static GeometryLabelApplyMissingResult Apply(
            string operationId,
            bool savePerformed = false) =>
            new(
                SchemaVersion: "1.0",
                TaskType: "geometry.label-apply-missing",
                OperationId: operationId,
                DrawingContext: "current_drafting_context",
                StartedAt: DateTimeOffset.UtcNow,
                CompletedAt: DateTimeOffset.UtcNow,
                Status: "SUCCESS",
                CreatedCount: 12,
                CreateFailedCount: 0,
                PostValidLabelCount: 12,
                PostMissingCount: 0,
                PostDuplicateCount: 0,
                PostCreatedValidCount: 12,
                PostCreatedPropertyErrorCount: 0,
                PostExistingMatchErrorCount: 0,
                PostExistingPropertyDriftCount: 0,
                PostInspectionErrorCount: 0,
                DrawingWritePerformed: true,
                DrawingWriteCount: 12,
                ManualRecoveryRequired: false,
                CreatedRuntimeHandles: Array.Empty<string>(),
                FailedOperationIds: Array.Empty<string>(),
                SavePerformed: savePerformed,
                PreAlreadyPresentCount: 0,
                PreMissingCount: 12,
                PreDuplicateTextCount: 0,
                PreInspectionErrorCount: 0,
                CreatedOperationIds: Array.Empty<string>(),
                ExistingPropertyDrifts: Array.Empty<GeometryLabelPropertyDrift>());
    }
}
