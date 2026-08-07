using System.Security;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
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

    private async void ExecuteReadOnlyPlan_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_viewModel.CanExecuteReadOnlyPlan)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            "即将把当前单一只读计划映射为固定 Console 白名单命令。\n\n" +
            "提交后需要在 Tribon 当前图纸中运行 Start.py 恰好一次。\n" +
            "该操作不会重新调用模型，不会写入图纸数据库，也不会执行 SAVEWORK。\n\n" +
            "确认继续？",
            "确认执行确定性只读计划",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.No);

        if (answer == MessageBoxResult.Yes)
        {
            await _viewModel.ExecuteReadOnlyPlanAsync();
            ScrollConversationToEnd();
        }
    }

    private async void RunPlanPreflight_Click(
        object sender,
        RoutedEventArgs e)
    {
        await _viewModel.RunLabelPreflightFromPlanAsync();
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
