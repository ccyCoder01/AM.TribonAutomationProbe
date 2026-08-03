namespace AM.TribonAutomationProbe.Core;

public sealed class AssistantResultFormatter
{
    public string FormatTaskResult(
        AssistantPlannedTask task,
        object result) =>
        result switch
        {
            GeometryDetectionResult value => FormatDetection(value),
            GeometryHighlightResult value => FormatHighlight(value),
            GeometryHighlightClearResult value => FormatClear(value),
            GeometryLabelPreflightResult value => FormatPreflight(value),
            GeometryLabelApplyMissingResult value => FormatApply(value),
            _ => $"任务 {task.TaskType} 已返回结果。"
        };

    public string FormatRun(
        AssistantTaskPlan? plan,
        AssistantTaskState state,
        IReadOnlyList<AssistantTaskExecutionResult> taskResults,
        AssistantExecutionError? error = null)
    {
        if (state == AssistantTaskState.AwaitingClarification)
        {
            return plan?.Message ?? "需要进一步明确要执行的任务。";
        }

        if (state == AssistantTaskState.AwaitingConfirmation)
        {
            return plan?.Message ?? "该任务需要确认后才能执行。";
        }

        if (state == AssistantTaskState.Cancelled)
        {
            return "任务已取消，未继续执行。";
        }

        if (state == AssistantTaskState.Failed)
        {
            return error is null
                ? "任务执行失败。"
                : $"任务执行失败：{error.Code}，{error.Message}";
        }

        if (taskResults.Count == 0)
        {
            return "任务已完成，没有需要报告的执行结果。";
        }

        return string.Join(Environment.NewLine, taskResults.Select(x => x.Summary));
    }

    private static string FormatDetection(GeometryDetectionResult value)
    {
        var liftingBeams = Count(value, GeometryObjectCategory.LIFTING_BEAM);
        var liftingLugs = Count(value, GeometryObjectCategory.LIFTING_LUG);
        var pipeFront = Count(value, GeometryObjectCategory.PIPE_FLANGE_FRONT);
        var pipeSide = Count(value, GeometryObjectCategory.PIPE_FLANGE_SIDE);
        var structural = Count(value, GeometryObjectCategory.STRUCTURAL_FLANGE);
        var flanges = pipeFront + pipeSide + structural;

        return $"已识别 {value.Objects.Count} 个目标对象：吊梁 {liftingBeams} 个，吊耳 {liftingLugs} 个，法兰 {flanges} 个。" +
               $"共分配 {value.Diagnostics.AssignedUniqueContourCount} 个轮廓，冲突 {value.Diagnostics.ConflictHandleCount} 个。" +
               FormatSafety(value.DrawingWritePerformed, value.SavePerformed);
    }

    private static string FormatHighlight(GeometryHighlightResult value) =>
        $"已高亮 {value.HighlightedObjectCount} 个对象，共 {value.HighlightSuccessCount}/{value.HighlightedHandleCount} 个轮廓成功，" +
        $"缺失 {value.MissingHandleCount} 个，失败 {value.HighlightFailureCount} 个。" +
        FormatSafety(value.DrawingWritePerformed, value.SavePerformed);

    private static string FormatClear(GeometryHighlightClearResult value) =>
        value.Cleared
            ? "已清除当前高亮。" + FormatSafety(value.DrawingWritePerformed, value.SavePerformed)
            : "未能确认当前高亮已清除。" + FormatSafety(value.DrawingWritePerformed, value.SavePerformed);

    private static string FormatPreflight(GeometryLabelPreflightResult value) =>
        $"对象标签检查完成：已存在 {value.PreAlreadyPresentCount} 个，缺失 {value.PreMissingCount} 个，" +
        $"重复 {value.PreDuplicateTextCount} 个，文字冲突 {value.PreTextConflictCount} 个，检查错误 {value.PreInspectionErrorCount} 个。" +
        FormatSafety(value.DrawingWritePerformed, value.SavePerformed);

    private static string FormatApply(GeometryLabelApplyMissingResult value) =>
        $"缺失标签处理完成：创建 {value.CreatedCount} 个，创建失败 {value.CreateFailedCount} 个，" +
        $"写后有效标签 {value.PostValidLabelCount} 个，缺失 {value.PostMissingCount} 个，重复 {value.PostDuplicateCount} 个。" +
        (value.ManualRecoveryRequired ? "需要人工恢复。" : "不需要人工恢复。") +
        FormatSafety(value.DrawingWritePerformed, value.SavePerformed);

    private static int Count(
        GeometryDetectionResult value,
        GeometryObjectCategory category) =>
        value.Objects.Count(x => x.Category == category);

    private static string FormatSafety(
        bool drawingWritePerformed,
        bool savePerformed) =>
        $"本次{(drawingWritePerformed ? "已修改" : "未修改")}图纸，" +
        $"{(savePerformed ? "已执行保存" : "未执行保存")}。";
}
