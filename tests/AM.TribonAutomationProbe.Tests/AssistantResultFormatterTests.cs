using AM.TribonAutomationProbe.Core;
using Xunit;

namespace AM.TribonAutomationProbe.Tests;

public sealed class AssistantResultFormatterTests
{
    private readonly AssistantResultFormatter _formatter = new();

    [Fact]
    public void DetectionSummaryContainsCountsAndSafetyState()
    {
        var objects = new[]
        {
            Object("LB-01", GeometryObjectCategory.LIFTING_BEAM),
            Object("LB-02", GeometryObjectCategory.LIFTING_BEAM),
            Object("LL-01", GeometryObjectCategory.LIFTING_LUG),
            Object("PF-01", GeometryObjectCategory.PIPE_FLANGE_FRONT),
            Object("PF-SIDE-01", GeometryObjectCategory.PIPE_FLANGE_SIDE),
            Object("SF-01", GeometryObjectCategory.STRUCTURAL_FLANGE)
        };

        var result = new GeometryDetectionResult(
            "1.0",
            "geometry.detect",
            "op-1",
            "current_drafting_context",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "succeeded",
            false,
            objects,
            new GeometryObjectDetectionDiagnostics(100, 90, 10, 0, 0),
            false);

        var summary = _formatter.FormatTaskResult(
            Planned(AssistantIntent.DetectGeometry, "geometry.detect"),
            result);

        Assert.Contains("6 个目标对象", summary, StringComparison.Ordinal);
        Assert.Contains("吊梁 2 个", summary, StringComparison.Ordinal);
        Assert.Contains("吊耳 1 个", summary, StringComparison.Ordinal);
        Assert.Contains("法兰 3 个", summary, StringComparison.Ordinal);
        Assert.Contains("90 个轮廓", summary, StringComparison.Ordinal);
        Assert.Contains("未修改图纸", summary, StringComparison.Ordinal);
        Assert.Contains("未执行保存", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplySummaryReportsCreationAndManualRecovery()
    {
        var result = new GeometryLabelApplyMissingResult(
            SchemaVersion: "1.0",
            TaskType: "geometry.label-apply-missing",
            OperationId: "op-1",
            DrawingContext: "current_drafting_context",
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow,
            Status: "PARTIAL_FAILURE",
            CreatedCount: 1,
            CreateFailedCount: 1,
            PostValidLabelCount: 1,
            PostMissingCount: 1,
            PostDuplicateCount: 0,
            PostCreatedValidCount: 1,
            PostCreatedPropertyErrorCount: 0,
            PostExistingMatchErrorCount: 0,
            PostExistingPropertyDriftCount: 0,
            PostInspectionErrorCount: 0,
            DrawingWritePerformed: true,
            DrawingWriteCount: 1,
            ManualRecoveryRequired: true,
            CreatedRuntimeHandles: Array.Empty<string>(),
            FailedOperationIds: ["label:LB-02"],
            SavePerformed: false);

        var summary = _formatter.FormatTaskResult(
            Planned(
                AssistantIntent.ApplyMissingLabels,
                "geometry.label-apply-missing"),
            result);

        Assert.Contains("创建 1 个", summary, StringComparison.Ordinal);
        Assert.Contains("创建失败 1 个", summary, StringComparison.Ordinal);
        Assert.Contains("需要人工恢复", summary, StringComparison.Ordinal);
        Assert.Contains("未执行保存", summary, StringComparison.Ordinal);
    }

    private static AssistantPlannedTask Planned(
        AssistantIntent intent,
        string taskType) =>
        new(
            1,
            intent,
            taskType,
            AssistantTaskRisk.ReadOnly,
            false,
            false,
            new Dictionary<string, string>());

    private static DetectedGeometryObject Object(
        string id,
        GeometryObjectCategory category) =>
        new(
            id,
            category,
            "test",
            new LayoutRectangle(0, 0, 10, 10),
            Array.Empty<string>(),
            Array.Empty<string>(),
            0,
            new GeometryFeatureSummary());
}
