using System.Security;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using AM.TribonAutomationProbe.Core;
using AM.TribonAutomationProbe.Desktop.Services;
using AM.TribonAutomationProbe.Desktop.ViewModels;

namespace AM.TribonAutomationProbe.Desktop;

public partial class MainWindow : Window
{
    private readonly AssistantConversationViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        var labelWorkflow = new ObjectLabelWorkflowViewModel(
            new ConsoleWorkflowClient(
                new BridgeResultMonitor()));

        _viewModel = new AssistantConversationViewModel(
            new ConsoleAssistantWorkflowClient(),
            labelWorkflow,
            new ConsoleAssistantReadOnlyPlanExecutionClient(
                new BridgeResultMonitor()));

        DataContext = _viewModel;
    }

    private void BrowseConsole_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择已发布的 Console 可执行文件",
            Filter =
                "AM.TribonAutomationProbe Console (*.exe)|*.exe|" +
                "All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.LabelWorkflow.ConsolePath = dialog.FileName;
        }
    }

    private async void Interpret_Click(
        object sender,
        RoutedEventArgs e)
    {
        await InterpretFromUiAsync();
    }

    private async void ExecutePlan_Click(
        object sender,
        RoutedEventArgs e)
    {
        await ExecutePlanFromUiAsync();
    }

    private async Task ExecutePlanFromUiAsync()
    {
        if (!_viewModel.CanExecuteCurrentPlan)
        {
            return;
        }

        switch (_viewModel.PlanExecutionRoute)
        {
            case AssistantPlanExecutionRoute.DeterministicReadOnly:
            {
                var taskCount =
                    _viewModel.CurrentInterpretation?.Plan.Tasks.Count ?? 0;

                if (taskCount <= 0)
                {
                    return;
                }

                var answer = MessageBox.Show(
                    this,
                    $"即将按 Sequence 顺序执行当前 {taskCount} 个确定性只读任务。\n\n" +
                    "每个任务仍通过已验证的单任务 Console 白名单执行器逐个提交；" +
                    "FileBridge 同一时刻只允许一个请求。\n" +
                    $"每出现一个已接受请求，都需要在 Tribon 当前图纸中运行 Start.py 恰好一次；" +
                    $"完整成功时预计共 {taskCount} 次。\n" +
                    "任何任务失败或取消后立即停止，不会提交后续任务。\n" +
                    "执行阶段不会重新调用模型，不会写入图纸数据库，也不会执行 SAVEWORK。\n\n" +
                    "确认继续？",
                    "确认执行确定性只读任务序列",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information,
                    MessageBoxResult.No);

                if (answer == MessageBoxResult.Yes)
                {
                    await _viewModel.ExecuteCurrentPlanAsync();
                }

                break;
            }

            case AssistantPlanExecutionRoute.LabelPreflight:
                await _viewModel.ExecuteCurrentPlanAsync();
                break;

            case AssistantPlanExecutionRoute.LabelApply:
            {
                var preflight = _viewModel.LabelWorkflow.PreflightResult;

                if (preflight is null ||
                    !_viewModel.CanApplyFromPlan)
                {
                    return;
                }

                var message =
                    $"即将创建 {preflight.PreMissingCount} 个缺失标签。\n\n" +
                    $"Preflight ID:\n{preflight.OperationId}\n\n" +
                    $"Plan Hash:\n{preflight.PlanHash}\n\n" +
                    "本次 Apply 会修改当前图纸，但不会自动保存。\n" +
                    "提交后仍需在 Tribon 中运行 Start.py 恰好一次。\n\n" +
                    "确认继续？";

                var answer = MessageBox.Show(
                    this,
                    message,
                    "确认受控 Apply",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (answer == MessageBoxResult.Yes)
                {
                    var authorization =
                        _viewModel.CreateApplyAuthorizationFromPlan();

                    if (authorization is null)
                    {
                        return;
                    }

                    await _viewModel.ExecuteCurrentPlanAsync(
                        authorization);
                }

                break;
            }

            default:
                return;
        }

        ScrollConversationToEnd();
    }

    private async void RunPreflight_Click(
        object sender,
        RoutedEventArgs e)
    {
        await _viewModel.LabelWorkflow.RunPreflightAsync();
    }

    private async void Apply_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel.PlanExecutionRoute ==
                AssistantPlanExecutionRoute.LabelApply &&
            _viewModel.CanExecuteCurrentPlan)
        {
            await ExecutePlanFromUiAsync();
            return;
        }

        var workflow = _viewModel.LabelWorkflow;
        var preflight = workflow.PreflightResult;

        if (preflight is null ||
            !workflow.CanApply)
        {
            return;
        }

        var message =
            $"即将创建 {preflight.PreMissingCount} 个缺失标签。\n\n" +
            $"Preflight ID:\n{preflight.OperationId}\n\n" +
            $"Plan Hash:\n{preflight.PlanHash}\n\n" +
            "本次 Apply 会修改当前图纸，但不会自动保存。\n" +
            "提交后仍需在 Tribon 中运行 Start.py 恰好一次。\n\n" +
            "确认继续？";

        var answer = MessageBox.Show(
            this,
            message,
            "确认受控 Apply",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer == MessageBoxResult.Yes)
        {
            await workflow.ApplyAsync();
            _viewModel.RecordApplyResult();
            ScrollConversationToEnd();
        }
    }

    private void Cancel_Click(
        object sender,
        RoutedEventArgs e)
    {
        _viewModel.CancelActiveOperation();
    }

    private void ClearConversation_Click(
        object sender,
        RoutedEventArgs e)
    {
        _viewModel.ClearConversation();
        ScrollConversationToEnd();
    }

    private async void UserInput_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter ||
            !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        e.Handled = true;
        await InterpretFromUiAsync();
    }

    private async Task InterpretFromUiAsync()
    {
        SecureString? authorizationSecret = null;

        try
        {
            if (_viewModel.UseRealModel)
            {
                authorizationSecret =
                    AssistantApiTokenBox.SecurePassword.Copy();
                authorizationSecret.MakeReadOnly();
            }

            await _viewModel.InterpretAsync(
                authorizationSecret);
        }
        finally
        {
            authorizationSecret?.Dispose();
            AssistantApiTokenBox.Clear();
            ScrollConversationToEnd();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.CancelActiveOperation();
        base.OnClosed(e);
    }

    private void ScrollConversationToEnd()
    {
        ConversationScrollViewer.UpdateLayout();
        ConversationScrollViewer.ScrollToEnd();
    }
}
