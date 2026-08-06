using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security;
using AM.TribonAutomationProbe.Core;
using AM.TribonAutomationProbe.Desktop.Models;
using AM.TribonAutomationProbe.Desktop.Services;

namespace AM.TribonAutomationProbe.Desktop.ViewModels;

public sealed class AssistantConversationViewModel : INotifyPropertyChanged
{
    private readonly IAssistantWorkflowClient _assistantClient;
    private CancellationTokenSource? _interpretationCancellation;
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

    public AssistantConversationViewModel(
        IAssistantWorkflowClient assistantClient,
        ObjectLabelWorkflowViewModel labelWorkflow)
    {
        _assistantClient = assistantClient ??
            throw new ArgumentNullException(nameof(assistantClient));
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

    public bool IsBusy => IsInterpreting || LabelWorkflow.IsBusy;

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

            return plan.ContainsWrite
                ? "计划包含图纸写入。必须先执行标签只读检查，并继续使用精确 preflight 绑定和显式确认；不会自动 SAVEWORK。"
                : "计划仅包含只读任务；当前增量不会从自然语言直接调用 Tribon。";
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
            : "计划仅包含只读任务，当前不会从自然语言直接调用 Tribon。";

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
        OnPropertyChanged(nameof(IsSingleLabelPlan));
        OnPropertyChanged(nameof(CanRunLabelPreflightFromPlan));
        OnPropertyChanged(nameof(CanApplyFromPlan));
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
