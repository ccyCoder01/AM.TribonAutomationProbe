using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using AM.TribonAutomationProbe.Core;
using AM.TribonAutomationProbe.Desktop.Models;
using AM.TribonAutomationProbe.Desktop.Services;

namespace AM.TribonAutomationProbe.Desktop.ViewModels;

public sealed class ObjectLabelWorkflowViewModel : INotifyPropertyChanged
{
    private readonly IConsoleWorkflowClient _client;
    private CancellationTokenSource? _activeCancellation;

    private string _consolePath;
    private string _bridgeRoot;
    private int _timeoutMs = 600000;
    private int _pollIntervalMs = 200;
    private bool _isBusy;
    private bool _isProgressIndeterminate;
    private bool _applyAcknowledged;
    private double _progressPercent;
    private string _statusMessage = "等待执行只读检查。";
    private string _errorMessage = string.Empty;
    private ObjectLabelWorkflowStage _stage =
        ObjectLabelWorkflowStage.Idle;
    private GeometryLabelPreflightResult? _preflightResult;
    private GeometryLabelApplyMissingResult? _applyResult;

    public ObjectLabelWorkflowViewModel(
        IConsoleWorkflowClient client)
    {
        _client = client ??
                  throw new ArgumentNullException(nameof(client));

        _consolePath = ResolveDefaultConsolePath();
        _bridgeRoot =
            Environment.GetEnvironmentVariable(
                "AM_TRIBON_BRIDGE_ROOT") ??
            @"C:\AM_TribonBridge";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<GeometryLabelPreflightItem>
        PreflightItems { get; } = new();

    public string ConsolePath
    {
        get => _consolePath;
        set
        {
            if (SetProperty(
                    ref _consolePath,
                    value ?? string.Empty))
            {
                InvalidateConfirmedPreflight();
            }
        }
    }

    public string BridgeRoot
    {
        get => _bridgeRoot;
        set
        {
            if (SetProperty(
                    ref _bridgeRoot,
                    value ?? string.Empty))
            {
                InvalidateConfirmedPreflight();
            }
        }
    }

    public int TimeoutMs
    {
        get => _timeoutMs;
        set
        {
            if (SetProperty(ref _timeoutMs, value))
            {
                InvalidateConfirmedPreflight();
            }
        }
    }

    public int PollIntervalMs
    {
        get => _pollIntervalMs;
        set
        {
            if (SetProperty(ref _pollIntervalMs, value))
            {
                InvalidateConfirmedPreflight();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanRunPreflight));
                OnPropertyChanged(nameof(CanApply));
                OnPropertyChanged(nameof(CanCancel));
            }
        }
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set =>
            SetProperty(
                ref _isProgressIndeterminate,
                value);
    }

    public bool ApplyAcknowledged
    {
        get => _applyAcknowledged;
        set
        {
            if (SetProperty(
                    ref _applyAcknowledged,
                    value))
            {
                OnPropertyChanged(nameof(CanApply));
            }
        }
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set =>
            SetProperty(
                ref _progressPercent,
                value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set =>
            SetProperty(
                ref _statusMessage,
                value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(
                    ref _errorMessage,
                    value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public ObjectLabelWorkflowStage Stage
    {
        get => _stage;
        private set
        {
            if (SetProperty(
                    ref _stage,
                    value))
            {
                OnPropertyChanged(nameof(HasWritablePreflight));
                OnPropertyChanged(nameof(CanApply));
            }
        }
    }

    public GeometryLabelPreflightResult? PreflightResult
    {
        get => _preflightResult;
        private set
        {
            if (SetProperty(
                    ref _preflightResult,
                    value))
            {
                RaisePreflightProperties();
            }
        }
    }

    public GeometryLabelApplyMissingResult? ApplyResult
    {
        get => _applyResult;
        private set
        {
            if (SetProperty(
                    ref _applyResult,
                    value))
            {
                RaiseApplyProperties();
            }
        }
    }

    public bool CanRunPreflight => !IsBusy;

    public bool CanCancel => IsBusy;

    public bool HasError =>
        !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasPreflight =>
        PreflightResult is not null;

    public bool HasApplyResult =>
        ApplyResult is not null;

    public bool HasWritablePreflight =>
        Stage == ObjectLabelWorkflowStage.ReadyToApply &&
        PreflightResult is
        {
            Status: "SUCCESS",
            PreMissingCount: > 0,
            PreDuplicateTextCount: 0,
            PreTextConflictCount: 0,
            PreInspectionErrorCount: 0
        } &&
        (PreflightResult.ReadyOperationIds?.Count ?? 0) ==
        PreflightResult.PreMissingCount;

    public bool CanApply =>
        !IsBusy &&
        HasWritablePreflight &&
        ApplyAcknowledged;

    public string PreflightStatus =>
        PreflightResult?.Status ?? "-";

    public int AlreadyPresentCount =>
        PreflightResult?.PreAlreadyPresentCount ?? 0;

    public int MissingCount =>
        PreflightResult?.PreMissingCount ?? 0;

    public int DuplicateCount =>
        PreflightResult?.PreDuplicateTextCount ?? 0;

    public int TextConflictCount =>
        PreflightResult?.PreTextConflictCount ?? 0;

    public int InspectionErrorCount =>
        PreflightResult?.PreInspectionErrorCount ?? 0;

    public string PlanHash =>
        PreflightResult?.PlanHash ?? "-";

    public string PreflightOperationId =>
        PreflightResult?.OperationId ?? "-";

    public string ApplyStatus =>
        ApplyResult?.Status ?? "-";

    public int CreatedCount =>
        ApplyResult?.CreatedCount ?? 0;

    public int CreateFailedCount =>
        ApplyResult?.CreateFailedCount ?? 0;

    public int DrawingWriteCount =>
        ApplyResult?.DrawingWriteCount ?? 0;

    public bool DrawingWritePerformed =>
        ApplyResult?.DrawingWritePerformed ?? false;

    public bool SavePerformed =>
        ApplyResult?.SavePerformed ?? false;

    public bool ManualSaveRequired =>
        ApplyResult is
        {
            Status: "SUCCESS",
            DrawingWritePerformed: true,
            SavePerformed: false
        };

    public string ManualSaveGuidance =>
        ManualSaveRequired
            ? "Apply 已完成，但没有自动保存。请先在 Tribon 中复核标签文字、位置、样式和遮挡；确认无误后使用 File → Save 手动保存一次。"
            : "当前没有待执行的手动保存步骤。";

    public async Task RunPreflightAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ResetForPreflight();
        var cancellation = new CancellationTokenSource();
        _activeCancellation = cancellation;
        IsBusy = true;
        Stage = ObjectLabelWorkflowStage.Validating;

        var progress = new Progress<WorkflowProgress>(
            UpdateProgress);

        try
        {
            var result = await _client.RunPreflightAsync(
                CreateSettings(),
                progress,
                cancellation.Token);

            PreflightResult = result;

            foreach (var item in result.Items)
            {
                PreflightItems.Add(item);
            }

            ProgressPercent = 100;
            IsProgressIndeterminate = false;

            if (string.Equals(
                    result.Status,
                    "BLOCKED",
                    StringComparison.Ordinal))
            {
                Stage = ObjectLabelWorkflowStage.Completed;
                StatusMessage =
                    "只读检查被阻止。请处理重复文字、文字冲突或检查错误；不要执行 Apply。";
            }
            else if (result.PreMissingCount == 0)
            {
                Stage = ObjectLabelWorkflowStage.Completed;
                StatusMessage =
                    "所有目标标签均已存在，不需要执行 Apply。";
            }
            else
            {
                Stage = ObjectLabelWorkflowStage.ReadyToApply;
                StatusMessage =
                    $"只读检查完成：{result.PreMissingCount} 个标签可创建。请核对计划后再授权 Apply。";
            }
        }
        catch (OperationCanceledException)
        {
            Stage = ObjectLabelWorkflowStage.Cancelled;
            StatusMessage = "操作已取消。";
            IsProgressIndeterminate = false;
        }
        catch (Exception ex)
        {
            Stage = ObjectLabelWorkflowStage.Failed;
            ErrorMessage = ex.Message;
            StatusMessage = "只读检查失败。";
            IsProgressIndeterminate = false;
        }
        finally
        {
            if (ReferenceEquals(
                    _activeCancellation,
                    cancellation))
            {
                _activeCancellation = null;
            }

            cancellation.Dispose();
            IsBusy = false;
        }
    }

    public async Task ApplyAsync()
    {
        if (!CanApply ||
            PreflightResult is null)
        {
            ErrorMessage =
                "Apply 尚未获得有效的只读检查与明确确认。";
            return;
        }

        var confirmedPreflight = PreflightResult;
        ApplyAcknowledged = false;
        ApplyResult = null;
        ErrorMessage = string.Empty;
        var cancellation = new CancellationTokenSource();
        _activeCancellation = cancellation;
        IsBusy = true;
        Stage = ObjectLabelWorkflowStage.Applying;

        var progress = new Progress<WorkflowProgress>(
            UpdateProgress);

        try
        {
            var result = await _client.RunApplyAsync(
                CreateSettings(),
                confirmedPreflight,
                progress,
                cancellation.Token);

            ApplyResult = result;
            ProgressPercent = 100;
            IsProgressIndeterminate = false;
            Stage = ObjectLabelWorkflowStage.Completed;

            StatusMessage = result.CreatedCount > 0
                ? $"Apply 完成：已创建 {result.CreatedCount} 个标签，失败 {result.CreateFailedCount} 个。请执行视觉复核，暂勿自动保存。"
                : "Apply 返回已完成状态，没有新增标签。";
        }
        catch (OperationCanceledException)
        {
            Stage = ObjectLabelWorkflowStage.Cancelled;
            StatusMessage = "Apply 已取消。请检查 FileBridge 状态后再决定下一步。";
            IsProgressIndeterminate = false;
        }
        catch (Exception ex)
        {
            Stage = ObjectLabelWorkflowStage.Failed;
            ErrorMessage = ex.Message;
            StatusMessage =
                "Apply 失败。不要盲目重复运行 Start.py 或重新提交写入请求。";
            IsProgressIndeterminate = false;
        }
        finally
        {
            if (ReferenceEquals(
                    _activeCancellation,
                    cancellation))
            {
                _activeCancellation = null;
            }

            cancellation.Dispose();
            IsBusy = false;
        }
    }

    public void CancelActiveOperation()
    {
        _activeCancellation?.Cancel();
    }

    private ConsoleWorkflowSettings CreateSettings() =>
        new(
            ConsolePath,
            BridgeRoot,
            TimeoutMs,
            PollIntervalMs);

    private void UpdateProgress(WorkflowProgress value)
    {
        ProgressPercent = Math.Clamp(
            value.Percent,
            0,
            100);
        StatusMessage = value.Message;
        IsProgressIndeterminate =
            value.IsIndeterminate;

        if (value.IsIndeterminate)
        {
            Stage = Stage == ObjectLabelWorkflowStage.Applying
                ? ObjectLabelWorkflowStage.Applying
                : ObjectLabelWorkflowStage.WaitingForWorker;
        }
        else if (value.Percent >= 80 &&
                 value.Percent < 100)
        {
            Stage = ObjectLabelWorkflowStage.ParsingResult;
        }
    }

    private void ResetForPreflight()
    {
        PreflightItems.Clear();
        PreflightResult = null;
        ApplyResult = null;
        ApplyAcknowledged = false;
        ErrorMessage = string.Empty;
        ProgressPercent = 0;
        IsProgressIndeterminate = false;
        StatusMessage = "正在准备只读检查。";
    }

    private void InvalidateConfirmedPreflight()
    {
        if (IsBusy ||
            PreflightResult is null)
        {
            return;
        }

        PreflightItems.Clear();
        PreflightResult = null;
        ApplyResult = null;
        ApplyAcknowledged = false;
        Stage = ObjectLabelWorkflowStage.Idle;
        ProgressPercent = 0;
        IsProgressIndeterminate = false;
        StatusMessage =
            "运行配置已更改。必须重新执行只读检查。";
        ErrorMessage = string.Empty;
    }

    private void RaisePreflightProperties()
    {
        OnPropertyChanged(nameof(HasPreflight));
        OnPropertyChanged(nameof(HasWritablePreflight));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(PreflightStatus));
        OnPropertyChanged(nameof(AlreadyPresentCount));
        OnPropertyChanged(nameof(MissingCount));
        OnPropertyChanged(nameof(DuplicateCount));
        OnPropertyChanged(nameof(TextConflictCount));
        OnPropertyChanged(nameof(InspectionErrorCount));
        OnPropertyChanged(nameof(PlanHash));
        OnPropertyChanged(nameof(PreflightOperationId));
    }

    private void RaiseApplyProperties()
    {
        OnPropertyChanged(nameof(HasApplyResult));
        OnPropertyChanged(nameof(ApplyStatus));
        OnPropertyChanged(nameof(CreatedCount));
        OnPropertyChanged(nameof(CreateFailedCount));
        OnPropertyChanged(nameof(DrawingWriteCount));
        OnPropertyChanged(nameof(DrawingWritePerformed));
        OnPropertyChanged(nameof(SavePerformed));
        OnPropertyChanged(nameof(ManualSaveRequired));
        OnPropertyChanged(nameof(ManualSaveGuidance));
    }

    private static string ResolveDefaultConsolePath()
    {
        var configured =
            Environment.GetEnvironmentVariable(
                "AM_TRIBON_PROBE_CONSOLE");

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var packaged = Path.Combine(
            AppContext.BaseDirectory,
            "console",
            "AM.TribonAutomationProbe.Console.exe");

        if (File.Exists(packaged))
        {
            return packaged;
        }

        return Path.Combine(
            AppContext.BaseDirectory,
            "AM.TribonAutomationProbe.Console.exe");
    }

    private bool SetProperty<T>(
        ref T storage,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(
                storage,
                value))
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
