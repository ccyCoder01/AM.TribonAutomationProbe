using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security;
using AM.TribonAutomationProbe.Core;
using AM.TribonAutomationProbe.Desktop.Models;
using AM.TribonAutomationProbe.Desktop.Services;

namespace AM.TribonAutomationProbe.Desktop.ViewModels;

public sealed class AssistantConversationViewModel : INotifyPropertyChanged
{
    private readonly IAssistantWorkflowClient _assistantClient;
    private readonly IAssistantReadOnlyPlanExecutionClient
        _readOnlyExecutionClient;
    private CancellationTokenSource? _interpretationCancellation;
    private CancellationTokenSource? _readOnlyExecutionCancellation;
    private string _userInput = string.Empty;
    private bool _isInterpreting;
    private string _errorMessage = string.Empty;
    private bool _useRealModel;
    private string _assistantBaseUrl =
        "https://api.yygu.cn/v3/llm.chat/chat/completions";
    private string _assistantModel = "deepseek/deepseek-v4-pro";
    private AssistantAuthorizationMode _authorizationMode =
        AssistantAuthorizationMode.BearerToken;
    private AssistantInterpretationEnvelope? _currentInterpretation;
    private bool _isExecutingReadOnlyPlan;
    private double _readOnlyExecutionProgress;
    private bool _isReadOnlyExecutionProgressIndeterminate;
    private string _readOnlyExecutionStatus =
        "尚未执行确定性只读计划。";
    private AssistantTaskExecutionResult? _readOnlyExecutionResult;

    public AssistantConversationViewModel(
        IAssistantWorkflowClient assistantClient,
        ObjectLabelWorkflowViewModel labelWorkflow,
        IAssistantReadOnlyPlanExecutionClient? readOnlyExecutionClient = null)
    {
        _assistantClient = assistantClient ??
            throw new ArgumentNullException(nameof(assistantClient));
        _readOnlyExecutionClient = readOnlyExecutionClient ??
            new ConsoleAssistantReadOnlyPlanExecutionClient();
        LabelWorkflow = labelWorkflow ??
            throw new ArgumentNullException(nameof(labelWorkflow));

        LabelWorkflow.PropertyChanged += LabelWorkflow_PropertyChanged;

        Messages.Add(
            new AssistantConversationMessage(
                "assistant",
                "请输入船舶设计任务。我会先生成受控执行计划，不会直接修改图纸，也不会自动执行 SAVEWORK。",
                DateTimeOffset.Now));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObjectLabelWorkflowViewModel LabelWorkflow { get; }

    public ObservableCollection<AssistantConversationMessage> Messages { get; } =
        new();

    public ObservableCollection<AssistantPlanTaskViewState> PlanTasks { get; } =
        new();

    public string UserInput
    {
        get => _userInput;
        set
        {
            if (SetProperty(ref _userInput, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanInterpret));
            }
        }
    }

    public bool UseRealModel
    {
        get => _useRealModel;
        set
        {
            if (SetProperty(ref _useRealModel, value))
            {
                OnPropertyChanged(nameof(ModelConfigurationSummary));
                OnPropertyChanged(nameof(CanInterpret));
            }
        }
    }

    public string AssistantBaseUrl
    {
        get => _assistantBaseUrl;
        set
        {
            if (SetProperty(
                    ref _assistantBaseUrl,
                    value ?? string.Empty))
            {
                OnPropertyChanged(nameof(ModelConfigurationSummary));
                OnPropertyChanged(nameof(CanInterpret));
            }
        }
    }

    public string AssistantModel
    {
        get => _assistantModel;
        set
        {
            if (SetProperty(
                    ref _assistantModel,
                    value ?? string.Empty))
            {
                OnPropertyChanged(nameof(ModelConfigurationSummary));
                OnPropertyChanged(nameof(CanInterpret));
            }
        }
    }

    public AssistantAuthorizationMode AuthorizationMode
    {
        get => _authorizationMode;
        set
        {
            if (SetProperty(ref _authorizationMode, value))
            {
                OnPropertyChanged(nameof(ModelConfigurationSummary));
            }
        }
    }

    public string ModelConfigurationSummary =>
        CreateProviderSettings().DisplayName;

    public bool IsInterpreting
    {
        get => _isInterpreting;
        private set
        {
            if (SetProperty(ref _isInterpreting, value))
            {
                RaiseBusyProperties();
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public AssistantInterpretationEnvelope? CurrentInterpretation
    {
        get => _currentInterpretation;
        private set
        {
            if (SetProperty(ref _currentInterpretation, value))
            {
                RaisePlanProperties();
            }
        }
    }

    public bool IsExecutingReadOnlyPlan
    {
        get => _isExecutingReadOnlyPlan;
        private set
        {
            if (SetProperty(ref _isExecutingReadOnlyPlan, value))
            {
                RaiseBusyProperties();
                OnPropertyChanged(nameof(ShowReadOnlyExecutionPanel));
            }
        }
    }

    public double ReadOnlyExecutionProgress
    {
        get => _readOnlyExecutionProgress;
        private set => SetProperty(ref _readOnlyExecutionProgress, value);
    }

    public bool IsReadOnlyExecutionProgressIndeterminate
    {
        get => _isReadOnlyExecutionProgressIndeterminate;
        private set => SetProperty(
            ref _isReadOnlyExecutionProgressIndeterminate,
            value);
    }

    public string ReadOnlyExecutionStatus
    {
        get => _readOnlyExecutionStatus;
        private set => SetProperty(
            ref _readOnlyExecutionStatus,
            value ?? string.Empty);
    }

    public AssistantTaskExecutionResult? ReadOnlyExecutionResult
    {
        get => _readOnlyExecutionResult;
        private set
        {
            if (SetProperty(ref _readOnlyExecutionResult, value))
            {
                OnPropertyChanged(nameof(HasReadOnlyExecutionResult));
                OnPropertyChanged(nameof(ReadOnlyExecutionSummary));
                OnPropertyChanged(nameof(ShowReadOnlyExecutionPanel));
            }
        }
    }

    public bool HasReadOnlyExecutionResult =>
        ReadOnlyExecutionResult is not null;

    public string ReadOnlyExecutionSummary =>
        ReadOnlyExecutionResult?.Summary ??
        "尚未收到确定性只读执行回执。";

    public bool CanExecuteReadOnlyPlan =>
        !IsBusy &&
        TryGetExecutableReadOnlyTasks(out _);

    public bool ShowReadOnlyExecutionPanel =>
        CanExecuteReadOnlyPlan ||
        IsExecutingReadOnlyPlan ||
        HasReadOnlyExecutionResult;

    public string ReadOnlyExecutionButtonText =>
        GetSinglePlanTask()?.Intent switch
        {
            AssistantIntent.DetectGeometry => "执行对象识别",
            AssistantIntent.HighlightLifting => "执行吊装对象高亮",
            AssistantIntent.HighlightFlanges => "执行法兰高亮",
            AssistantIntent.ClearHighlight => "执行清除高亮",
            _ => "执行确定性只读计划"
        };

    public bool IsBusy =>
        IsInterpreting ||
        IsExecutingReadOnlyPlan ||
        LabelWorkflow.IsBusy;

    public bool CanInterpret =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(UserInput) &&
        (!UseRealModel ||
         (!string.IsNullOrWhiteSpace(AssistantBaseUrl) &&
          !string.IsNullOrWhiteSpace(AssistantModel)));

    public bool CanCancel => IsBusy;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasPlan => CurrentInterpretation is not null;

    public string PlanId => CurrentInterpretation?.Plan.PlanId ?? "-";

    public string PlanState =>
        CurrentInterpretation?.Plan.State.ToString() ?? "-";

    public string PlanMessage =>
        CurrentInterpretation?.Plan.Message ??
        "尚未生成执行计划。";

    public string ModelSummary => CurrentInterpretation is null
        ? "-"
        : $"{CurrentInterpretation.Interpretation.Provider}/" +
          CurrentInterpretation.Interpretation.Model;

    public bool PlanContainsWrite =>
        CurrentInterpretation?.Plan.ContainsWrite ?? false;

    public bool PlanRequiresConfirmation =>
        CurrentInterpretation?.Plan.RequiresConfirmation ?? false;

    public string PlanSafetySummary
    {
        get
        {
            var plan = CurrentInterpretation?.Plan;

            if (plan is null)
            {
                return "尚未生成计划。";
            }

            if (plan.State == AssistantTaskState.AwaitingClarification)
            {
                return plan.Message;
            }

            if (plan.ContainsWrite)
            {
                return "计划包含图纸写入。必须先执行标签只读检查，并继续使用精确 preflight 绑定和显式确认；不会自动 SAVEWORK。";
            }

            if (!TryGetExecutableReadOnlyTasks(out var tasks))
            {
                return "当前计划不满足确定性只读任务序列执行门禁，仅保留为计划预览。";
            }

            var taskNames = string.Join(
                "、",
                tasks.Select(task => GetDisplayName(task.Intent)));

            return
                $"计划可按 Sequence 顺序通过固定白名单命令执行 {tasks.Count} 个只读任务：{taskNames}。" +
                "每个已接受的 FileBridge 请求均需在 Tribon 当前图纸中运行 Start.py 恰好一次；" +
                "执行阶段不会重新调用模型，不会写入图纸数据库，也不会执行 SAVEWORK。";
        }
    }

    public bool IsSingleLabelPlan
    {
        get
        {
            var task = GetSinglePlanTask();
            return task?.Intent is
                AssistantIntent.PreflightLabels or
                AssistantIntent.ApplyMissingLabels;
        }
    }

    public bool CanRunLabelPreflightFromPlan
    {
        get
        {
            var state = CurrentInterpretation?.Plan.State;
            return !IsBusy &&
                   IsSingleLabelPlan &&
                   state is
                       AssistantTaskState.Planned or
                       AssistantTaskState.AwaitingConfirmation;
        }
    }

    public bool CanApplyFromPlan
    {
        get
        {
            var task = GetSinglePlanTask();
            return !IsBusy &&
                   task?.Intent == AssistantIntent.ApplyMissingLabels &&
                   LabelWorkflow.CanApply;
        }
    }

    public async Task InterpretAsync(
        SecureString? authorizationSecret = null)
    {
        if (!CanInterpret)
        {
            return;
        }

        var input = UserInput.Trim();
        UserInput = string.Empty;
        ErrorMessage = string.Empty;
        ClearPlan();

        Messages.Add(
            new AssistantConversationMessage(
                "user",
                input,
                DateTimeOffset.Now));

        var cancellation = new CancellationTokenSource();
        _interpretationCancellation = cancellation;
        IsInterpreting = true;

        try
        {
            var result = await _assistantClient.InterpretAsync(
                CreateSettings(),
                CreateProviderSettings(),
                authorizationSecret,
                input,
                cancellation.Token);

            CurrentInterpretation = result;
            PopulatePlanTasks(result.Plan);

            var response = result.Plan.State ==
                           AssistantTaskState.AwaitingClarification
                ? result.Plan.Message
                : BuildPlanResponse(result);

            Messages.Add(
                new AssistantConversationMessage(
                    "assistant",
                    response,
                    DateTimeOffset.Now));
        }
        catch (OperationCanceledException)
        {
            Messages.Add(
                new AssistantConversationMessage(
                    "system",
                    "自然语言解释已取消，没有执行任何图纸操作。",
                    DateTimeOffset.Now));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Messages.Add(
                new AssistantConversationMessage(
                    "system",
                    $"计划生成失败：{ex.Message}",
                    DateTimeOffset.Now));
        }
        finally
        {
            if (ReferenceEquals(
                    _interpretationCancellation,
                    cancellation))
            {
                _interpretationCancellation = null;
            }

            cancellation.Dispose();
            IsInterpreting = false;
        }
    }

    public async Task ExecuteReadOnlyPlanAsync()
    {
        if (!CanExecuteReadOnlyPlan ||
            CurrentInterpretation is null ||
            !TryGetExecutableReadOnlyTasks(out var tasks))
        {
            ErrorMessage =
                "当前计划不满足确定性只读任务序列执行门禁，或系统正忙。";
            return;
        }

        var plan = CurrentInterpretation.Plan;
        var settings = CreateSettings();

        ErrorMessage = string.Empty;
        ReadOnlyExecutionResult = null;
        ReadOnlyExecutionProgress = 0;
        IsReadOnlyExecutionProgressIndeterminate = false;
        ReadOnlyExecutionStatus =
            $"正在准备 {tasks.Count} 个确定性只读任务。";

        Messages.Add(
            new AssistantConversationMessage(
                "assistant",
                $"已验证 {tasks.Count} 个确定性只读任务，将按 Sequence 升序逐个提交。" +
                " 每个已接受的 FileBridge 请求都需要在 Tribon 当前图纸中运行 Start.py 恰好一次；" +
                " 任一任务失败或取消后立即停止，不提交后续任务。" +
                " 执行阶段不会重新调用模型，不会写入图纸数据库，也不会执行 SAVEWORK。",
                DateTimeOffset.Now));

        var cancellation = new CancellationTokenSource();
        _readOnlyExecutionCancellation = cancellation;
        IsExecutingReadOnlyPlan = true;

        try
        {
            for (var index = 0; index < tasks.Count; index++)
            {
                cancellation.Token.ThrowIfCancellationRequested();

                var task = tasks[index];
                var singleTaskPlan =
                    CreateSingleTaskReadOnlyPlan(
                        plan,
                        task);

                ReadOnlyExecutionStatus =
                    $"任务 {task.Sequence}/{tasks.Count}：" +
                    $"正在准备 {task.TaskType}。";

                var taskIndex = index;
                var progress = new Progress<WorkflowProgress>(
                    value =>
                        UpdateReadOnlyExecutionProgress(
                            new WorkflowProgress(
                                MapReadOnlyTaskProgress(
                                    value.Percent,
                                    taskIndex,
                                    tasks.Count),
                                $"任务 {task.Sequence}/{tasks.Count}：" +
                                value.Message,
                                value.IsIndeterminate)));

                var result =
                    await _readOnlyExecutionClient.ExecuteAsync(
                        settings,
                        singleTaskPlan,
                        progress,
                        cancellation.Token);

                result = result with
                {
                    Sequence = task.Sequence
                };

                if (result.State != AssistantTaskState.Completed ||
                    result.DrawingWritePerformed ||
                    result.SavePerformed)
                {
                    throw new InvalidDataException(
                        "The deterministic read-only task result violated " +
                        "the multi-task orchestration safety contract.");
                }

                ReadOnlyExecutionResult = result;
                ReadOnlyExecutionProgress =
                    ((index + 1d) / tasks.Count) * 100d;
                IsReadOnlyExecutionProgressIndeterminate = false;
                ReadOnlyExecutionStatus =
                    $"任务 {task.Sequence}/{tasks.Count} 完成：{result.Summary}";

                Messages.Add(
                    new AssistantConversationMessage(
                        "assistant",
                        $"任务 {task.Sequence}/{tasks.Count} 回执：{result.Summary} " +
                        $"图纸写入={result.DrawingWritePerformed}，" +
                        $"自动保存={result.SavePerformed}。",
                        DateTimeOffset.Now));
            }

            ReadOnlyExecutionProgress = 100;
            IsReadOnlyExecutionProgressIndeterminate = false;
            ReadOnlyExecutionStatus =
                $"确定性只读任务序列已完成：{tasks.Count}/{tasks.Count}。";

            Messages.Add(
                new AssistantConversationMessage(
                    "assistant",
                    $"确定性只读任务序列执行完成：{tasks.Count}/{tasks.Count}。" +
                    " 全程未重新调用模型，未写入图纸数据库，未执行 SAVEWORK。",
                    DateTimeOffset.Now));
        }
        catch (OperationCanceledException)
        {
            ReadOnlyExecutionStatus =
                "确定性只读任务序列已取消；后续任务未提交。" +
                " 请检查 FileBridge 状态后再决定下一步。";
            IsReadOnlyExecutionProgressIndeterminate = false;

            Messages.Add(
                new AssistantConversationMessage(
                    "system",
                    ReadOnlyExecutionStatus,
                    DateTimeOffset.Now));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ReadOnlyExecutionStatus =
                "确定性只读任务序列执行失败；已停止，后续任务未提交。" +
                " 不要盲目重复运行 Start.py 或重新提交请求。";
            IsReadOnlyExecutionProgressIndeterminate = false;

            Messages.Add(
                new AssistantConversationMessage(
                    "system",
                    $"确定性只读任务序列执行失败：{ex.Message}",
                    DateTimeOffset.Now));
        }
        finally
        {
            if (ReferenceEquals(
                    _readOnlyExecutionCancellation,
                    cancellation))
            {
                _readOnlyExecutionCancellation = null;
            }

            cancellation.Dispose();
            IsExecutingReadOnlyPlan = false;
        }
    }

    public async Task RunLabelPreflightFromPlanAsync()
    {
        if (!CanRunLabelPreflightFromPlan)
        {
            ErrorMessage =
                "当前计划不是可执行的单一标签计划，或系统正忙。";
            return;
        }

        ErrorMessage = string.Empty;
        Messages.Add(
            new AssistantConversationMessage(
                "assistant",
                "已将计划交给现有确定性标签工作流。Console 提交请求后，请在 Tribon 当前图纸中运行 Start.py 恰好一次。",
                DateTimeOffset.Now));

        await LabelWorkflow.RunPreflightAsync();

        if (LabelWorkflow.HasError)
        {
            ErrorMessage = LabelWorkflow.ErrorMessage;
            Messages.Add(
                new AssistantConversationMessage(
                    "system",
                    $"标签只读检查失败：{LabelWorkflow.ErrorMessage}",
                    DateTimeOffset.Now));
            return;
        }

        var result = LabelWorkflow.PreflightResult;

        if (result is null)
        {
            ErrorMessage = "标签只读检查没有返回结果。";
            return;
        }

        var response =
            $"标签只读检查完成：已存在 {result.PreAlreadyPresentCount} 个，" +
            $"待创建 {result.PreMissingCount} 个，重复文字 {result.PreDuplicateTextCount} 个，" +
            $"文字冲突 {result.PreTextConflictCount} 个，检查错误 {result.PreInspectionErrorCount} 个。";

        if (CurrentInterpretation?.Plan.ContainsWrite == true &&
            LabelWorkflow.HasWritablePreflight)
        {
            response +=
                " 该自然语言计划包含写入；请核对 Plan Hash 和对象列表，再勾选授权并确认 Apply。";
        }

        Messages.Add(
            new AssistantConversationMessage(
                "assistant",
                response,
                DateTimeOffset.Now));

        RaisePlanProperties();
    }

    public void RecordApplyResult()
    {
        var result = LabelWorkflow.ApplyResult;

        if (result is null)
        {
            return;
        }

        Messages.Add(
            new AssistantConversationMessage(
                "assistant",
                $"Apply 回执：创建 {result.CreatedCount} 个，失败 {result.CreateFailedCount} 个；" +
                $"图纸写入 {result.DrawingWriteCount} 个，自动保存={result.SavePerformed}。" +
                " 请在 Tribon 中执行视觉复核，确认后手动保存。",
                DateTimeOffset.Now));

        RaisePlanProperties();
    }

    public void CancelActiveOperation()
    {
        _interpretationCancellation?.Cancel();
        _readOnlyExecutionCancellation?.Cancel();
        LabelWorkflow.CancelActiveOperation();
    }

    public void ClearConversation()
    {
        if (IsBusy)
        {
            return;
        }

        Messages.Clear();
        ClearPlan();
        ErrorMessage = string.Empty;
        Messages.Add(
            new AssistantConversationMessage(
                "assistant",
                "对话已清空。请输入新的船舶设计任务。",
                DateTimeOffset.Now));
    }

    private ConsoleWorkflowSettings CreateSettings() =>
        new(
            LabelWorkflow.ConsolePath,
            LabelWorkflow.BridgeRoot,
            LabelWorkflow.TimeoutMs,
            LabelWorkflow.PollIntervalMs);

    private AssistantProviderSessionSettings CreateProviderSettings() =>
        UseRealModel
            ? new AssistantProviderSessionSettings(
                AssistantProviderMode.OpenAiCompatible,
                AssistantBaseUrl,
                AssistantModel,
                AuthorizationMode)
            : AssistantProviderSessionSettings.RuleBased;

    private AssistantPlannedTask? GetSinglePlanTask()
    {
        var tasks = CurrentInterpretation?.Plan.Tasks;
        return tasks is { Count: 1 }
            ? tasks[0]
            : null;
    }

    private bool TryGetExecutableReadOnlyTasks(
        out IReadOnlyList<AssistantPlannedTask> tasks)
    {
        tasks = Array.Empty<AssistantPlannedTask>();
        var plan = CurrentInterpretation?.Plan;

        if (plan is null ||
            plan.State != AssistantTaskState.Planned ||
            plan.ContainsWrite ||
            plan.RequiresConfirmation ||
            plan.AutoSave ||
            plan.Tasks.Count == 0)
        {
            return false;
        }

        var ordered = plan.Tasks
            .OrderBy(x => x.Sequence)
            .ToArray();

        for (var index = 0; index < ordered.Length; index++)
        {
            var candidate = ordered[index];

            if (candidate.Sequence != index + 1)
            {
                return false;
            }

            var singleTaskPlan =
                CreateSingleTaskReadOnlyPlan(
                    plan,
                    candidate);

            try
            {
                _ = ConsoleAssistantReadOnlyPlanExecutionClient.ValidatePlan(
                    singleTaskPlan);
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        tasks = ordered;
        return true;
    }

    private static AssistantTaskPlan CreateSingleTaskReadOnlyPlan(
        AssistantTaskPlan sourcePlan,
        AssistantPlannedTask sourceTask) =>
        sourcePlan with
        {
            Tasks = new[]
            {
                sourceTask with
                {
                    Sequence = 1
                }
            }
        };

    private static double MapReadOnlyTaskProgress(
        double taskPercent,
        int zeroBasedTaskIndex,
        int taskCount)
    {
        if (taskCount <= 0 ||
            zeroBasedTaskIndex < 0 ||
            zeroBasedTaskIndex >= taskCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(zeroBasedTaskIndex));
        }

        var normalizedTaskPercent =
            Math.Clamp(
                taskPercent,
                0d,
                100d);

        return Math.Clamp(
            (
                zeroBasedTaskIndex * 100d +
                normalizedTaskPercent
            ) / taskCount,
            0d,
            100d);
    }

    private void UpdateReadOnlyExecutionProgress(
        WorkflowProgress value)
    {
        ReadOnlyExecutionProgress = Math.Clamp(
            value.Percent,
            0,
            100);
        ReadOnlyExecutionStatus = value.Message;
        IsReadOnlyExecutionProgressIndeterminate =
            value.IsIndeterminate;
    }

    private void PopulatePlanTasks(AssistantTaskPlan plan)
    {
        PlanTasks.Clear();

        foreach (var task in plan.Tasks.OrderBy(x => x.Sequence))
        {
            PlanTasks.Add(
                new AssistantPlanTaskViewState(
                    task.Sequence,
                    task.Intent,
                    task.TaskType,
                    GetDisplayName(task.Intent),
                    task.Risk,
                    task.Risk == AssistantTaskRisk.DrawingWrite
                        ? "图纸写入"
                        : "只读",
                    task.RequiresConfirmation,
                    task.RequiresConfirmation
                        ? "需要预检与显式确认"
                        : "无需写入确认"));
        }
    }

    private void ClearPlan()
    {
        PlanTasks.Clear();
        CurrentInterpretation = null;
        ReadOnlyExecutionResult = null;
        ReadOnlyExecutionProgress = 0;
        IsReadOnlyExecutionProgressIndeterminate = false;
        ReadOnlyExecutionStatus =
            "尚未执行确定性只读计划。";
    }

    private static string BuildPlanResponse(
        AssistantInterpretationEnvelope result)
    {
        var plan = result.Plan;
        var taskNames = string.Join(
            "、",
            plan.Tasks
                .OrderBy(x => x.Sequence)
                .Select(x => GetDisplayName(x.Intent)));
        var safety = plan.ContainsWrite
            ? "计划包含写入，当前不会执行；必须先完成只读检查并显式确认。"
            : plan.Tasks.Count > 0 &&
              plan.Tasks.All(task =>
                  task.Intent is (
                      AssistantIntent.DetectGeometry or
                      AssistantIntent.HighlightLifting or
                      AssistantIntent.HighlightFlanges or
                      AssistantIntent.ClearHighlight))
                ? "计划可由用户显式点击后按 Sequence 顺序映射为固定只读命令；不会重新调用模型。"
                : "计划当前仅用于预览。";

        return $"已生成 {plan.Tasks.Count} 个受控任务：{taskNames}。{safety}";
    }

    private static string GetDisplayName(AssistantIntent intent) =>
        intent switch
        {
            AssistantIntent.DetectGeometry => "识别当前图纸对象",
            AssistantIntent.HighlightLifting => "高亮吊梁和吊耳",
            AssistantIntent.HighlightFlanges => "高亮法兰",
            AssistantIntent.ClearHighlight => "清除高亮",
            AssistantIntent.PreflightLabels => "检查对象标签",
            AssistantIntent.ApplyMissingLabels => "创建缺失对象标签",
            _ => "不支持的任务"
        };

    private void LabelWorkflow_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is
            nameof(ObjectLabelWorkflowViewModel.IsBusy) or
            nameof(ObjectLabelWorkflowViewModel.CanApply) or
            nameof(ObjectLabelWorkflowViewModel.HasWritablePreflight) or
            nameof(ObjectLabelWorkflowViewModel.PreflightResult) or
            nameof(ObjectLabelWorkflowViewModel.ApplyResult))
        {
            RaiseBusyProperties();
            RaisePlanProperties();
        }
    }

    private void RaiseBusyProperties()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanInterpret));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRunLabelPreflightFromPlan));
        OnPropertyChanged(nameof(CanApplyFromPlan));
        OnPropertyChanged(nameof(CanExecuteReadOnlyPlan));
        OnPropertyChanged(nameof(ShowReadOnlyExecutionPanel));
    }

    private void RaisePlanProperties()
    {
        OnPropertyChanged(nameof(HasPlan));
        OnPropertyChanged(nameof(PlanId));
        OnPropertyChanged(nameof(PlanState));
        OnPropertyChanged(nameof(PlanMessage));
        OnPropertyChanged(nameof(ModelSummary));
        OnPropertyChanged(nameof(PlanContainsWrite));
        OnPropertyChanged(nameof(PlanRequiresConfirmation));
        OnPropertyChanged(nameof(PlanSafetySummary));
        OnPropertyChanged(nameof(ReadOnlyExecutionButtonText));
        OnPropertyChanged(nameof(IsSingleLabelPlan));
        OnPropertyChanged(nameof(CanRunLabelPreflightFromPlan));
        OnPropertyChanged(nameof(CanApplyFromPlan));
        OnPropertyChanged(nameof(CanExecuteReadOnlyPlan));
        OnPropertyChanged(nameof(ShowReadOnlyExecutionPanel));
    }

    private bool SetProperty<T>(
        ref T storage,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}
