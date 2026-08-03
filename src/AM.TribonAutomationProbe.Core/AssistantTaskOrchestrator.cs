namespace AM.TribonAutomationProbe.Core;

public sealed class AssistantTaskOrchestrator(
    IAssistantLanguageModel languageModel,
    AssistantTaskPlanner planner,
    IGeometryAutomationAdapter geometry,
    AssistantResultFormatter formatter)
{
    public const string ProductName = "船舶设计智能助手";

    public async Task<AssistantRunResult> RunAsync(
        AssistantConversationContext context,
        AssistantExecutionAuthorization authorization,
        IProgress<AssistantProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorization);

        var runId = "RUN-" + Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;
        var updates = new List<AssistantProgressUpdate>();
        var taskResults = new List<AssistantTaskExecutionResult>();
        AssistantTaskPlan? plan = null;
        AssistantInterpretation? interpretation = null;
        var progressSequence = 0;

        void Report(
            AssistantTaskState state,
            string message,
            string? taskType = null)
        {
            var update = new AssistantProgressUpdate(
                ++progressSequence,
                state,
                message,
                DateTimeOffset.UtcNow,
                taskType);

            updates.Add(update);
            progress?.Report(update);
        }

        AssistantRunResult Complete(
            AssistantTaskState state,
            string summary,
            AssistantExecutionError? error = null) =>
            new(
                SchemaVersion: "1.0",
                ProductName: ProductName,
                RunId: runId,
                StartedAt: startedAt,
                CompletedAt: DateTimeOffset.UtcNow,
                State: state,
                Plan: plan,
                Progress: updates.ToArray(),
                TaskResults: taskResults.ToArray(),
                Summary: summary,
                Error: error,
                Model: interpretation is null
                    ? null
                    : new AssistantModelExecutionInfo(
                        interpretation.Provider,
                        interpretation.Model,
                        interpretation.RequestId,
                        interpretation.ResponseId,
                        interpretation.LatencyMs,
                        interpretation.FallbackUsed,
                        interpretation.FallbackReason));

        try
        {
            Report(AssistantTaskState.Received, "已接收用户指令。");
            Report(AssistantTaskState.Interpreting, "正在理解指令并生成结构化意图。");

            interpretation = await languageModel.InterpretAsync(
                context,
                cancellationToken);

            if (interpretation.FallbackUsed)
            {
                Report(
                    AssistantTaskState.Interpreting,
                    $"主模型不可用，已降级到 {interpretation.Provider}/{interpretation.Model}。原因：{interpretation.FallbackReason}。");
            }

            plan = planner.CreatePlan(context, interpretation);

            if (plan.State == AssistantTaskState.AwaitingClarification)
            {
                Report(
                    AssistantTaskState.AwaitingClarification,
                    plan.Message);

                return Complete(
                    AssistantTaskState.AwaitingClarification,
                    formatter.FormatRun(
                        plan,
                        AssistantTaskState.AwaitingClarification,
                        taskResults));
            }

            Report(
                AssistantTaskState.Planned,
                $"已生成 {plan.Tasks.Count} 个受控任务。任务计划不自动执行 SAVEWORK。");

            if (plan.RequiresConfirmation)
            {
                var missing = new List<string>();

                if (!authorization.WriteConfirmed)
                {
                    missing.Add("写操作确认");
                }

                if (!authorization.AllowWrite)
                {
                    missing.Add("写权限授权");
                }

                if (string.IsNullOrWhiteSpace(
                        authorization.ConfirmedPreflightOperationId))
                {
                    missing.Add("已确认的预检操作标识");
                }

                if (string.IsNullOrWhiteSpace(
                        authorization.ConfirmedPlanHash))
                {
                    missing.Add("已确认的计划哈希");
                }

                if ((authorization.ConfirmedOperationIds?.Count ?? 0) == 0)
                {
                    missing.Add("已确认的补标操作集合");
                }

                if (missing.Count > 0)
                {
                    var message =
                        plan.Message +
                        " 当前缺少：" +
                        string.Join("、", missing) +
                        "。";

                    plan = plan with
                    {
                        State = AssistantTaskState.AwaitingConfirmation,
                        Message = message
                    };

                    Report(
                        AssistantTaskState.AwaitingConfirmation,
                        message);

                    return Complete(
                        AssistantTaskState.AwaitingConfirmation,
                        formatter.FormatRun(
                            plan,
                            AssistantTaskState.AwaitingConfirmation,
                            taskResults));
                }
            }

            plan = plan with
            {
                State = AssistantTaskState.Queued,
                Message = "任务计划已授权并进入执行队列。"
            };

            Report(
                AssistantTaskState.Queued,
                "任务计划已通过白名单、确认和权限检查。");

            foreach (var task in plan.Tasks.OrderBy(x => x.Sequence))
            {
                cancellationToken.ThrowIfCancellationRequested();

                Report(
                    AssistantTaskState.WaitingForTribon,
                    $"正在等待 Tribon 执行任务 {task.TaskType}。",
                    task.TaskType);

                Report(
                    AssistantTaskState.Executing,
                    $"正在执行任务 {task.TaskType}。",
                    task.TaskType);

                var rawResult = await ExecuteTaskAsync(
                    plan,
                    task,
                    authorization,
                    cancellationToken);

                Report(
                    AssistantTaskState.Verifying,
                    $"正在校验任务 {task.TaskType} 的结构化结果。",
                    task.TaskType);

                var envelope = ValidateResult(task, rawResult);
                var summary = formatter.FormatTaskResult(task, rawResult);

                taskResults.Add(
                    new AssistantTaskExecutionResult(
                        Sequence: task.Sequence,
                        TaskType: task.TaskType,
                        State: AssistantTaskState.Completed,
                        Status: envelope.Status,
                        DrawingWritePerformed: envelope.DrawingWritePerformed,
                        SavePerformed: envelope.SavePerformed,
                        Summary: summary,
                        RawResult: rawResult));
            }

            plan = plan with
            {
                State = AssistantTaskState.Completed,
                Message = "任务计划已完成，且未自动执行 SAVEWORK。"
            };

            Report(
                AssistantTaskState.Completed,
                "全部任务执行并校验完成。");

            return Complete(
                AssistantTaskState.Completed,
                formatter.FormatRun(
                    plan,
                    AssistantTaskState.Completed,
                    taskResults));
        }
        catch (OperationCanceledException)
        {
            var error = new AssistantExecutionError(
                Code: "ASSISTANT_CANCELLED",
                Category: "cancellation",
                Message: "任务已取消。",
                Retryable: true);

            Report(
                AssistantTaskState.Cancelled,
                error.Message);

            return Complete(
                AssistantTaskState.Cancelled,
                formatter.FormatRun(
                    plan,
                    AssistantTaskState.Cancelled,
                    taskResults,
                    error),
                error);
        }
        catch (ProbeException ex)
        {
            var error = new AssistantExecutionError(
                ex.Code,
                ex.Category,
                ex.Message,
                ex.Retryable);

            Report(
                AssistantTaskState.Failed,
                $"任务失败：{ex.Code}，{ex.Message}");

            return Complete(
                AssistantTaskState.Failed,
                formatter.FormatRun(
                    plan,
                    AssistantTaskState.Failed,
                    taskResults,
                    error),
                error);
        }
        catch (Exception ex)
        {
            var error = new AssistantExecutionError(
                ProbeErrorCodes.InternalError,
                "execution",
                ex.Message,
                false);

            Report(
                AssistantTaskState.Failed,
                $"任务失败：{error.Code}，{error.Message}");

            return Complete(
                AssistantTaskState.Failed,
                formatter.FormatRun(
                    plan,
                    AssistantTaskState.Failed,
                    taskResults,
                    error),
                error);
        }
    }

    private async Task<object> ExecuteTaskAsync(
        AssistantTaskPlan plan,
        AssistantPlannedTask task,
        AssistantExecutionAuthorization authorization,
        CancellationToken cancellationToken)
    {
        var operationId = plan.PlanId + "-" + task.Sequence.ToString("00");

        return task.Intent switch
        {
            AssistantIntent.DetectGeometry =>
                await geometry.DetectAsync(
                    new GeometryDetectionRequest(
                        OperationId: operationId),
                    cancellationToken),

            AssistantIntent.HighlightLifting =>
                await geometry.HighlightAsync(
                    new GeometryHighlightRequest(
                        TaskType: task.TaskType,
                        OperationId: operationId,
                        Categories:
                        [
                            GeometryObjectCategory.LIFTING_BEAM,
                            GeometryObjectCategory.LIFTING_LUG
                        ]),
                    cancellationToken),

            AssistantIntent.HighlightFlanges =>
                await geometry.HighlightAsync(
                    new GeometryHighlightRequest(
                        TaskType: task.TaskType,
                        OperationId: operationId,
                        Categories:
                        [
                            GeometryObjectCategory.PIPE_FLANGE_FRONT,
                            GeometryObjectCategory.PIPE_FLANGE_SIDE,
                            GeometryObjectCategory.STRUCTURAL_FLANGE
                        ]),
                    cancellationToken),

            AssistantIntent.ClearHighlight =>
                await geometry.ClearHighlightAsync(
                    new GeometryHighlightClearRequest(
                        TaskType: task.TaskType,
                        OperationId: operationId),
                    cancellationToken),

            AssistantIntent.PreflightLabels =>
                await geometry.PreflightLabelsAsync(
                    new GeometryLabelPreflightRequest(
                        TaskType: task.TaskType,
                        OperationId: operationId),
                    cancellationToken),

            AssistantIntent.ApplyMissingLabels =>
                await geometry.ApplyMissingLabelsAsync(
                    new GeometryLabelApplyMissingRequest(
                        TaskType: task.TaskType,
                        OperationId: operationId,
                        AllowWrite: authorization.AllowWrite,
                        WriteConfirmed: authorization.WriteConfirmed,
                        ConfirmedPreflightOperationId:
                            authorization.ConfirmedPreflightOperationId ??
                            string.Empty,
                        ConfirmedPlanHash:
                            authorization.ConfirmedPlanHash ??
                            string.Empty,
                        ConfirmedOperationIds:
                            authorization.ConfirmedOperationIds),
                    cancellationToken),

            _ => throw new ProbeException(
                ProbeErrorCodes.UnsupportedAction,
                $"Unsupported assistant intent: {task.Intent}",
                "validation")
        };
    }

    private static AssistantResultEnvelope ValidateResult(
        AssistantPlannedTask task,
        object result)
    {
        var envelope = result switch
        {
            GeometryDetectionResult value => new AssistantResultEnvelope(
                value.TaskType,
                value.Status,
                value.DrawingWritePerformed,
                value.SavePerformed),

            GeometryHighlightResult value => new AssistantResultEnvelope(
                value.TaskType,
                value.Status,
                value.DrawingWritePerformed,
                value.SavePerformed),

            GeometryHighlightClearResult value => new AssistantResultEnvelope(
                value.TaskType,
                value.Status,
                value.DrawingWritePerformed,
                value.SavePerformed),

            GeometryLabelPreflightResult value => new AssistantResultEnvelope(
                value.TaskType,
                value.Status,
                value.DrawingWritePerformed,
                value.SavePerformed),

            GeometryLabelApplyMissingResult value => new AssistantResultEnvelope(
                value.TaskType,
                value.Status,
                value.DrawingWritePerformed,
                value.SavePerformed),

            _ => throw new ProbeException(
                ProbeErrorCodes.InvalidResultMessage,
                $"Unsupported assistant result type: {result.GetType().FullName}",
                "validation")
        };

        if (!string.Equals(
                envelope.TaskType,
                task.TaskType,
                StringComparison.Ordinal))
        {
            throw new ProbeException(
                ProbeErrorCodes.InvalidResultMessage,
                $"Assistant result taskType mismatch. Expected {task.TaskType}, actual {envelope.TaskType}.",
                "validation");
        }

        if (envelope.SavePerformed)
        {
            throw new ProbeException(
                ProbeErrorCodes.SaveFailed,
                "Assistant task unexpectedly performed SAVEWORK.",
                "safety");
        }

        if (task.Risk == AssistantTaskRisk.ReadOnly &&
            envelope.DrawingWritePerformed)
        {
            throw new ProbeException(
                ProbeErrorCodes.VerificationFailed,
                $"Read-only assistant task {task.TaskType} reported a drawing write.",
                "safety");
        }

        if (!IsAcceptedStatus(task.Intent, envelope.Status))
        {
            throw new ProbeException(
                ProbeErrorCodes.VerificationFailed,
                $"Assistant task {task.TaskType} returned non-accepted status {envelope.Status}.",
                "verification");
        }

        return envelope;
    }

    private static bool IsAcceptedStatus(
        AssistantIntent intent,
        string status)
    {
        if (intent == AssistantIntent.PreflightLabels)
        {
            return status.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase) ||
                   status.Equals("BLOCKED", StringComparison.OrdinalIgnoreCase);
        }

        if (intent == AssistantIntent.ApplyMissingLabels)
        {
            return status.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase) ||
                   status.Equals("ALREADY_COMPLETE", StringComparison.OrdinalIgnoreCase) ||
                   status.Equals("BLOCKED", StringComparison.OrdinalIgnoreCase);
        }

        return status.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("SUCCEEDED", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record AssistantResultEnvelope(
        string TaskType,
        string Status,
        bool DrawingWritePerformed,
        bool SavePerformed);
}
