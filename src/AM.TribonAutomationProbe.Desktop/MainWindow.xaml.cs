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
            labelWorkflow);

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
        await _viewModel.InterpretAsync();
        ScrollConversationToEnd();
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
        await _viewModel.InterpretAsync();
        ScrollConversationToEnd();
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
