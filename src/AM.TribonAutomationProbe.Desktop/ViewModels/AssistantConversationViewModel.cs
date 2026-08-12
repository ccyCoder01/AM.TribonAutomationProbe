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
    private bool _hasModelCredential;
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
    private AssistantProductExecutionState _executionState =
        AssistantProductExecutionState.Idle;
    private string _executionStatus =
        "等待船舶设计任务。";

    public AssistantConversationViewModel(
        IAssistantWorkflowClient assistantClient,
        ObjectLabelWorkflowViewModel labelWorkflow,
        IAssistantReadOnlyPlanExecutionClient? readOnlyExecutionClient = null,
        bool modelCredentialAvailable = true)
    {
        _assistantClient = assistantClient ??
            throw new ArgumentNullException(nameof(assistantClient));
        _readOnlyExecutionClient = readOnlyExecutionClient ??
            new ConsoleAssistantReadOnlyPlanExecutionClient();
        _hasModelCredential = modelCredentialAvailable;
        LabelWorkflow = labelWorkflow ??
            throw new ArgumentNullException(nameof(labelWorkflow));

        LabelWorkflow.PropertyChanged += LabelWorkflow_PropertyChanged;

        Messages.Add(
            new AssistantConversationMessage(
                "assistant",
                "请输入船舶设计任务。我会先理解你的指令并生成受控执行计划，不会直接修改图纸，也不会自动保存。",
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

    public bool HasModelCredential => _hasModelCredential;

    public string ModelCredentialStatus =>
        HasModelCredential
            ? "Token 已保存（Windows 当前用户加密）"
            : "Token 未配置";

    public string ModelSettingsHeader =>
        string.IsNullOrWhiteSpace(AssistantModel)
            ? "模型设置 · 未配置"
            : $"模型设置 · {AssistantModel} · " +
              (HasModelCredential ? "已配置" : "缺少 Token");

    public void SetModelCredentialAvailable(bool available)
    {
        if (!SetProperty(ref _hasModelCredential, available))
        {
            return;
        }

        OnPropertyChanged(nameof(ModelCredentialStatus));
        OnPropertyChanged(nameof(ModelSettingsHeader));
        OnPropertyChanged(nameof(CanInterpret));
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
                OnPropertyChanged(nameof(ModelSettingsHeader));
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
                OnPropertyChanged(nameof(ModelSettingsHeader));
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
        ReadOnlyExecutionResult is null &&
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

    public AssistantProductExecutionState ExecutionState
    {
        get => _executionState;
        private set
        {
            if (SetProperty(ref _executionState, value))
            {
                OnPropertyChanged(nameof(ExecutionStateText));
                OnPropertyChanged(nameof(IsAwaitingWriteConfirmation));
                OnPropertyChanged(nameof(IsExecutionTerminal));
            }
        }
    }

    public string ExecutionStateText =>
        ExecutionState switch
        {
            AssistantProductExecutionState.Idle => "等待任务",
            AssistantProductExecutionState.Planning => "正在理解",
            AssistantProductExecutionState.Validating => "正在检查",
            AssistantProductExecutionState.Executing => "正在执行",
            AssistantProductExecutionState.AwaitingWriteConfirmation =>
                "等待确认",
            AssistantProductExecutionState.ExecutingWrite => "正在创建标签",
            AssistantProductExecutionState.Completed => "已完成",
            AssistantProductExecutionState.Failed => "执行失败",
            AssistantProductExecutionState.Cancelled => "已取消",
            AssistantProductExecutionState.RuntimeUnavailable =>
                "执行通道不可用",
            _ => ExecutionState.ToString()
        };

    public string ExecutionStatus
    {
        get => _executionStatus;
        private set =>
            SetProperty(
                ref _executionStatus,
                value ?? string.Empty);
    }

    public bool IsAwaitingWriteConfirmation =>
        ExecutionState ==
        AssistantProductExecutionState.AwaitingWriteConfirmation;

    public bool IsExecutionTerminal =>
        ExecutionState is
            AssistantProductExecutionState.Completed or
            AssistantProductExecutionState.Failed or
            AssistantProductExecutionState.Cancelled or
            AssistantProductExecutionState.RuntimeUnavailable;

    public bool IsBusy =>
        IsInterpreting ||
        IsExecutingReadOnlyPlan ||
        LabelWorkflow.IsBusy;

    public bool CanInterpret =>
        !IsBusy &&
        HasModelCredential &&
        !string.IsNullOrWhiteSpace(UserInput) &&
        !string.IsNullOrWhiteSpace(AssistantBaseUrl) &&
        !string.IsNullOrWhiteSpace(AssistantModel);

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
                return "计划包含图纸写入。系统会先自动完成标签安全检查；检查通过后只在你明确确认一次后创建标签，且不会自动保存图纸。";
            }

            if (!TryGetExecutableReadOnlyTasks(out var tasks))
            {
                return "当前计划不满足确定性只读任务序列执行门禁，仅保留为计划预览。";
            }

            var taskNames = string.Join(
                "、",
                tasks.Select(task => GetDisplayName(task.Intent)));

            return
                $"计划将按顺序自动执行 {tasks.Count} 个只读任务：{taskNames}。" +
                "系统会通过受控 Tribon 执行通道处理；执行阶段不会重新调用模型，不会修改图纸，也不会自动保存。";
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

    public bool CanCreateLabelsFromPreflight =>
        !IsBusy &&
        LabelWorkflow.HasWritablePreflight &&
        !LabelWorkflow.HasApplyResult;

    public bool ShowCreateLabelsFromPreflight =>
        LabelWorkflow.HasWritablePreflight &&
        !LabelWorkflow.HasApplyResult;

    public AssistantPlanExecutionRoute PlanExecutionRoute =>
        GetPlanExecutionRoute();

    public bool CanExecuteCurrentPlan =>
        PlanExecutionRoute switch
        {
            AssistantPlanExecutionRoute.DeterministicReadOnly =>
                CanExecuteReadOnlyPlan,
            AssistantPlanExecutionRoute.LabelPreflight =>
                CanRunLabelPreflightFromPlan,
            AssistantPlanExecutionRoute.LabelApply =>
                CanApplyFromPlan,
            _ => false
        };

    public string PlanExecutionButtonText =>
        PlanExecutionRoute switch
        {
            AssistantPlanExecutionRoute.DeterministicReadOnly =>
                ReadOnlyExecutionButtonText,
            AssistantPlanExecutionRoute.LabelPreflight =>
                "执行标签安全检查",
            AssistantPlanExecutionRoute.LabelApply =>
                "创建缺失标签",
            _ => "当前计划仅预览"
        };

    public string PlanExecutionLifecycleSummary =>
        PlanExecutionRoute switch
        {
            AssistantPlanExecutionRoute.DeterministicReadOnly =>
                "计划将按顺序自动执行；执行期间不会重新调用模型，不会修改图纸，也不会自动保存。",
            AssistantPlanExecutionRoute.LabelPreflight =>
                "系统会自动执行标签安全检查；检查本身不会修改图纸。",
            AssistantPlanExecutionRoute.LabelApply =>
                "标签安全检查已完成；核对结果后点击“创建标签”即明确授权本次检查绑定的标签写入，且不会自动保存。",
            _ => "当前计划没有可执行的统一确定性路线，仅保留计划预览。"
        };

    public AssistantExecutionAuthorization? CreateApplyAuthorizationFromPlan()
    {
        if (!CanApplyFromPlan ||
            LabelWorkflow.PreflightResult is not { } preflight)
        {
            return null;
        }

        var operationIds =
            (preflight.ReadyOperationIds ?? Array.Empty<string>())
            .ToArray();

        return new AssistantExecutionAuthorization(
            AllowWrite: true,
            WriteConfirmed: true,
            ConfirmedPreflightOperationId: preflight.OperationId,
            ConfirmedPlanHash: preflight.PlanHash,
            ConfirmedOperationIds: operationIds);
    }

    public async Task ExecuteCurrentPlanAsync(
        AssistantExecutionAuthorization? authorization = null)
    {
        switch (PlanExecutionRoute)
        {
            case AssistantPlanExecutionRoute.DeterministicReadOnly:
                if (authorization is not null)
                {
                    throw new InvalidDataException(
                        "只读任务不接受图纸修改授权。");
                }

                await ExecuteReadOnlyPlanAsync();
                return;

            case AssistantPlanExecutionRoute.LabelPreflight:
                if (authorization is not null)
                {
                    throw new InvalidDataException(
                        "标签安全检查不接受图纸修改授权。");
                }

                await RunLabelPreflightFromPlanAsync();
                return;

            case AssistantPlanExecutionRoute.LabelApply:
                if (!CanApplyFromPlan ||
                    LabelWorkflow.PreflightResult is not { } preflight)
                {
                    ErrorMessage =
                        "当前标签创建任务尚未完成有效的标签安全检查和明确确认。";
                    return;
                }

                if (authorization is null)
                {
                    throw new InvalidDataException(
                        "标签创建需要与当前标签安全检查绑定的明确写入确认。");
                }

                ValidateApplyAuthorization(
                    authorization,
                    preflight);
                SetExecutionState(
                    AssistantProductExecutionState.ExecutingWrite,
                    $"正在创建 {preflight.PreMissingCount} 个标签；不会自动保存。");

                await LabelWorkflow.ApplyAsync();

                if (LabelWorkflow.HasError)
                {
                    ErrorMessage = LabelWorkflow.ErrorMessage;
                    SetExecutionState(
                        AssistantProductExecutionState.Failed,
                        "标签创建失败；请检查当前图纸状态。");
                    Messages.Add(
                        new AssistantConversationMessage(
                            "system",
                            $"标签创建失败：{LabelWorkflow.ErrorMessage}",
                            DateTimeOffset.Now));
                    RaisePlanProperties();
                    return;
                }

                RecordApplyResult();
                SetExecutionState(
                    AssistantProductExecutionState.Completed,
                    LabelWorkflow.SavePerformed
                        ? "标签创建完成，图纸已保存。"
                        : $"标签创建完成：{LabelWorkflow.CreatedCount} 个；图纸已修改，尚未保存。");
                return;

            default:
                ErrorMessage =
                    "当前计划没有可执行的统一确定性路线。";
                return;
        }
    }

    private void SetExecutionState(
        AssistantProductExecutionState state,
        string status)
    {
        ExecutionState = state;
        ExecutionStatus = status;
    }

    public async Task InterpretAsync(
        SecureString? authorizationSecret = null)
    {
        if (!CanInterpret)
        {
            return;
        }

        var input = UserInput.Trim();
        var shouldAutoExecuteValidatedPlan = false;
        AssistantConversationMessage? pendingAssistantMessage = null;

        UserInput = string.Empty;
        ErrorMessage = string.Empty;
        ClearPlan();
        SetExecutionState(
            AssistantProductExecutionState.Planning,
            "正在理解你的指令并生成执行计划…");

        Messages.Add(
            new AssistantConversationMessage(
                "user",
                input,
                DateTimeOffset.Now));

        pendingAssistantMessage =
            new AssistantConversationMessage(
                "assistant",
                "正在理解你的指令…",
                DateTimeOffset.Now);
        Messages.Add(pendingAssistantMessage);

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
            SetExecutionState(
                AssistantProductExecutionState.Validating,
                result.Plan.State ==
                    AssistantTaskState.AwaitingClarification
                    ? "需要补充信息后才能继续。"
                    : "执行计划已生成，正在检查安全边界。");

            var response = result.Plan.State ==
                           AssistantTaskState.AwaitingClarification
                ? result.Plan.Message
                : BuildPlanResponse(result);

            if (pendingAssistantMessage is not null)
            {
                Messages.Remove(pendingAssistantMessage);
                pendingAssistantMessage = null;
            }

            Messages.Add(
                new AssistantConversationMessage(
                    "assistant",
                    response,
                    DateTimeOffset.Now));

            if (result.Plan.State ==
                AssistantTaskState.AwaitingClarification)
            {
                SetExecutionState(
                    AssistantProductExecutionState.Idle,
                    result.Plan.Message);
            }
            else if (ShouldAutomaticallyExecuteValidatedPlan())
            {
                shouldAutoExecuteValidatedPlan = true;
                SetExecutionState(
                    AssistantProductExecutionState.Validating,
                    result.Plan.ContainsWrite
                        ? "安全检查通过，准备自动执行写入前只读检查。"
                        : "安全检查通过，准备自动执行只读任务。");
            }
            else if (result.Plan.ContainsWrite)
            {
                SetExecutionState(
                    AssistantProductExecutionState.Validating,
                    "写入计划已生成，但当前计划没有自动执行的安全路线。");
            }
            else
            {
                SetExecutionState(
                    AssistantProductExecutionState.Validating,
                    "计划已生成，但当前计划没有自动执行的确定性路线。");
            }
        }
        catch (OperationCanceledException)
        {
            if (pendingAssistantMessage is not null)
            {
                Messages.Remove(pendingAssistantMessage);
                pendingAssistantMessage = null;
            }

            SetExecutionState(
                AssistantProductExecutionState.Cancelled,
                "任务已取消，没有执行图纸写入。");
            Messages.Add(
                new AssistantConversationMessage(
                    "system",
                    "自然语言解释已取消，没有执行任何图纸操作。",
                    DateTimeOffset.Now));
        }
        catch (Exception ex)
        {
            if (pendingAssistantMessage is not null)
            {
                Messages.Remove(pendingAssistantMessage);
                pendingAssistantMessage = null;
            }

            ErrorMessage = ex.Message;
            SetExecutionState(
                AssistantProductExecutionState.Failed,
                "执行计划生成失败，本次任务没有执行。");
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

        if (shouldAutoExecuteValidatedPlan &&
            !HasError &&
            ExecutionState ==
                AssistantProductExecutionState.Validating)
        {
            await ExecuteValidatedPlanAutomaticallyAsync();
        }
    }

    private bool ShouldAutomaticallyExecuteValidatedPlan()
    {
        var plan = CurrentInterpretation?.Plan;

        if (plan is null ||
            plan.State is not (
                AssistantTaskState.Planned or
                AssistantTaskState.AwaitingConfirmation))
        {
            return false;
        }

        if (TryGetExecutableReadOnlyTasks(out _))
        {
            return true;
        }

        var task = GetSinglePlanTask();

        return task?.Intent is
            AssistantIntent.PreflightLabels or
            AssistantIntent.ApplyMissingLabels;
    }

    private async Task ExecuteValidatedPlanAutomaticallyAsync()
    {
        if (TryGetExecutableReadOnlyTasks(out _))
        {
            await ExecuteReadOnlyPlanAsync();
            return;
        }

        var task = GetSinglePlanTask();

        if (task?.Intent is
            AssistantIntent.PreflightLabels or
            AssistantIntent.ApplyMissingLabels)
        {
            await RunLabelPreflightFromPlanAsync();
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
                $"已验证 {tasks.Count} 个只读任务，将按顺序自动执行。" +
                " 系统会通过受控 Tribon 执行通道逐个处理；任一任务失败或取消后立即停止，不提交后续任务。" +
                " 执行阶段不会重新调用模型，不会修改图纸，也不会自动保存。",
                DateTimeOffset.Now));

        var cancellation = new CancellationTokenSource();
        _readOnlyExecutionCancellation = cancellation;
        IsExecutingReadOnlyPlan = true;
        SetExecutionState(
            AssistantProductExecutionState.Executing,
            $"正在执行 {tasks.Count} 个只读任务。");

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
                        "只读任务返回了不符合安全约束的结果，后续任务已停止。");
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
            SetExecutionState(
                AssistantProductExecutionState.Completed,
                $"只读任务已完成：{tasks.Count}/{tasks.Count}。");

            Messages.Add(
                new AssistantConversationMessage(
                    "assistant",
                    $"确定性只读任务序列执行完成：{tasks.Count}/{tasks.Count}。" +
                    " 全程未重新调用模型，未修改图纸，也未自动保存。",
                    DateTimeOffset.Now));
        }
        catch (OperationCanceledException)
        {
            SetExecutionState(
                AssistantProductExecutionState.Cancelled,
                "只读执行已取消，后续任务未提交。");
            ReadOnlyExecutionStatus =
                "只读任务已取消；后续任务未提交。" +
                " 如需继续，请重新发送任务或使用运行诊断检查 Tribon 执行通道。";
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
            SetExecutionState(
                AssistantProductExecutionState.Failed,
                "只读执行失败，后续任务未提交。");
            ReadOnlyExecutionStatus =
                "只读任务执行失败；已停止，后续任务未提交。" +
                " Tribon 执行通道保持安全停止状态，请先重新检测通道后再重试。";
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
        SetExecutionState(
            AssistantProductExecutionState.Executing,
            "正在执行标签安全检查…");
        Messages.Add(
            new AssistantConversationMessage(
                "assistant",
                "正在执行标签安全检查…",
                DateTimeOffset.Now));

        await LabelWorkflow.RunPreflightAsync();

        if (LabelWorkflow.HasError)
        {
            ErrorMessage = LabelWorkflow.ErrorMessage;
            SetExecutionState(
                AssistantProductExecutionState.Failed,
                "标签安全检查失败，本次没有执行写入。");
            Messages.Add(
                new AssistantConversationMessage(
                    "system",
                    $"标签安全检查失败：{LabelWorkflow.ErrorMessage}",
                    DateTimeOffset.Now));
            return;
        }

        var result = LabelWorkflow.PreflightResult;

        if (result is null)
        {
            ErrorMessage = "标签安全检查没有返回结果。";
            SetExecutionState(
                AssistantProductExecutionState.Failed,
                "标签安全检查没有返回有效结果。");
            return;
        }

        var response =
            $"标签安全检查完成：已存在 {result.PreAlreadyPresentCount} 个，" +
            $"待创建 {result.PreMissingCount} 个，重复文字 {result.PreDuplicateTextCount} 个，" +
            $"文字冲突 {result.PreTextConflictCount} 个，检查错误 {result.PreInspectionErrorCount} 个。";

        if (CurrentInterpretation?.Plan.ContainsWrite == true &&
            LabelWorkflow.HasWritablePreflight)
        {
            response +=
                " 该任务包含图纸写入；请核对待创建数量和对象列表，确认后再创建标签。";
            SetExecutionState(
                AssistantProductExecutionState.AwaitingWriteConfirmation,
                $"标签安全检查完成：{result.PreMissingCount} 个标签可创建，等待你的确认。");
        }
        else if (string.Equals(
                     result.Status,
                     "BLOCKED",
                     StringComparison.Ordinal))
        {
            SetExecutionState(
                AssistantProductExecutionState.Failed,
                "标签安全检查未通过，未执行图纸写入。");
        }
        else
        {
            SetExecutionState(
                AssistantProductExecutionState.Completed,
                "标签安全检查已完成，没有待确认的图纸写入。");
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
                $"标签创建完成：创建 {result.CreatedCount} 个，失败 {result.CreateFailedCount} 个。" +
                " 图纸已修改，尚未自动保存。请在 Tribon 中执行视觉复核，确认后手动保存。",
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
        SetExecutionState(
            AssistantProductExecutionState.Idle,
            "等待船舶设计任务。");
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
        new(
            AssistantProviderMode.OpenAiCompatible,
            AssistantBaseUrl,
            AssistantModel,
            AuthorizationMode);

    private AssistantPlanExecutionRoute GetPlanExecutionRoute()
    {
        if (TryGetExecutableReadOnlyTasks(out _))
        {
            return AssistantPlanExecutionRoute.DeterministicReadOnly;
        }

        var plan = CurrentInterpretation?.Plan;
        var task = GetSinglePlanTask();

        if (plan is null ||
            task is null ||
            plan.AutoSave ||
            plan.State is not (
                AssistantTaskState.Planned or
                AssistantTaskState.AwaitingConfirmation))
        {
            return AssistantPlanExecutionRoute.None;
        }

        if (task.Intent == AssistantIntent.PreflightLabels &&
            task.Risk == AssistantTaskRisk.ReadOnly &&
            !task.RequiresConfirmation)
        {
            return AssistantPlanExecutionRoute.LabelPreflight;
        }

        if (task.Intent == AssistantIntent.ApplyMissingLabels &&
            task.Risk == AssistantTaskRisk.DrawingWrite &&
            task.RequiresConfirmation &&
            plan.ContainsWrite &&
            plan.RequiresConfirmation)
        {
            if (LabelWorkflow.HasApplyResult)
            {
                return AssistantPlanExecutionRoute.None;
            }

            return LabelWorkflow.HasWritablePreflight
                ? AssistantPlanExecutionRoute.LabelApply
                : AssistantPlanExecutionRoute.LabelPreflight;
        }

        return AssistantPlanExecutionRoute.None;
    }

    private static void ValidateApplyAuthorization(
        AssistantExecutionAuthorization authorization,
        GeometryLabelPreflightResult preflight)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(preflight);

        if (!authorization.AllowWrite ||
            !authorization.WriteConfirmed)
        {
            throw new InvalidDataException(
                "标签创建缺少明确的图纸修改确认。");
        }

        if (!string.Equals(
                authorization.ConfirmedPreflightOperationId,
                preflight.OperationId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "标签创建确认已失效；当前标签安全检查已发生变化，请重新检查后确认。");
        }

        if (!string.Equals(
                authorization.ConfirmedPlanHash,
                preflight.PlanHash,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "标签创建确认已失效；当前标签计划已发生变化，请重新检查后确认。");
        }

        var expectedOperationIds =
            (preflight.ReadyOperationIds ?? Array.Empty<string>())
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var confirmedOperationIds =
            (authorization.ConfirmedOperationIds ??
             Array.Empty<string>())
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        if (expectedOperationIds.Length == 0 ||
            !expectedOperationIds.SequenceEqual(
                confirmedOperationIds,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "标签创建确认已失效；待创建标签集合已发生变化，请重新检查后确认。");
        }
    }

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
            ? "计划包含图纸写入；系统会先自动完成标签安全检查，写入前等待你的明确确认。"
            : plan.Tasks.Count > 0 &&
              plan.Tasks.All(task =>
                  task.Intent is (
                      AssistantIntent.DetectGeometry or
                      AssistantIntent.HighlightLifting or
                      AssistantIntent.HighlightFlanges or
                      AssistantIntent.ClearHighlight))
                ? "计划已验证，将按顺序自动执行；执行期间不会重新调用模型。"
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
        OnPropertyChanged(nameof(CanCreateLabelsFromPreflight));
        OnPropertyChanged(nameof(ShowCreateLabelsFromPreflight));
        OnPropertyChanged(nameof(CanExecuteReadOnlyPlan));
        OnPropertyChanged(nameof(PlanExecutionRoute));
        OnPropertyChanged(nameof(CanExecuteCurrentPlan));
        OnPropertyChanged(nameof(PlanExecutionButtonText));
        OnPropertyChanged(nameof(PlanExecutionLifecycleSummary));
        OnPropertyChanged(nameof(ShowReadOnlyExecutionPanel));
        OnPropertyChanged(nameof(ExecutionState));
        OnPropertyChanged(nameof(ExecutionStateText));
        OnPropertyChanged(nameof(ExecutionStatus));
        OnPropertyChanged(nameof(IsAwaitingWriteConfirmation));
        OnPropertyChanged(nameof(IsExecutionTerminal));
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
        OnPropertyChanged(nameof(CanCreateLabelsFromPreflight));
        OnPropertyChanged(nameof(ShowCreateLabelsFromPreflight));
        OnPropertyChanged(nameof(CanExecuteReadOnlyPlan));
        OnPropertyChanged(nameof(PlanExecutionRoute));
        OnPropertyChanged(nameof(CanExecuteCurrentPlan));
        OnPropertyChanged(nameof(PlanExecutionButtonText));
        OnPropertyChanged(nameof(PlanExecutionLifecycleSummary));
        OnPropertyChanged(nameof(ShowReadOnlyExecutionPanel));
        OnPropertyChanged(nameof(ExecutionState));
        OnPropertyChanged(nameof(ExecutionStateText));
        OnPropertyChanged(nameof(ExecutionStatus));
        OnPropertyChanged(nameof(IsAwaitingWriteConfirmation));
        OnPropertyChanged(nameof(IsExecutionTerminal));
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
