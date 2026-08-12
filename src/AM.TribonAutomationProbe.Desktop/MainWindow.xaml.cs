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
    private readonly AssistantModelConfigurationStore
        _modelConfigurationStore;

    public MainWindow()
    {
        InitializeComponent();

        _modelConfigurationStore =
            new AssistantModelConfigurationStore();

        var modelConfiguration =
            _modelConfigurationStore.LoadSnapshot();

        var labelWorkflow = new ObjectLabelWorkflowViewModel(
            new ConsoleWorkflowClient(
                new BridgeResultMonitor()));

        _viewModel = new AssistantConversationViewModel(
            new ConsoleAssistantWorkflowClient(),
            labelWorkflow,
            new ConsoleAssistantReadOnlyPlanExecutionClient(
                new BridgeResultMonitor()),
            modelConfiguration.HasCredential);

        if (!string.IsNullOrWhiteSpace(
                modelConfiguration.BaseUrl))
        {
            _viewModel.AssistantBaseUrl =
                modelConfiguration.BaseUrl;
        }

        if (!string.IsNullOrWhiteSpace(
                modelConfiguration.Model))
        {
            _viewModel.AssistantModel =
                modelConfiguration.Model;
        }

        _viewModel.SetModelCredentialAvailable(
            modelConfiguration.HasCredential);

        DataContext = _viewModel;

        _viewModel.Messages.CollectionChanged +=
            (_, _) =>
                Dispatcher.BeginInvoke(
                    new Action(ScrollConversationToEnd));
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
                    $"即将按顺序执行当前 {taskCount} 个只读任务。\n\n" +
                    "系统会通过受控 Tribon 执行通道自动逐个处理。\n" +
                    "任何任务失败或取消后立即停止，不会提交后续任务。\n" +
                    "执行阶段不会重新调用模型，不会修改图纸，也不会自动保存。\n\n" +
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
                    "该写入已绑定刚完成的标签安全检查和已核对对象列表。\n" +
                    "本次操作会修改当前图纸，但不会自动保存。\n\n" +
                    "确认创建？";

                var answer = MessageBox.Show(
                    this,
                    message,
                    "确认创建标签",
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
        var workflow = _viewModel.LabelWorkflow;
        var preflight = workflow.PreflightResult;

        if (preflight is null ||
            !_viewModel.CanCreateLabelsFromPreflight)
        {
            return;
        }

        // Clicking "创建标签" is the explicit write confirmation for the
        // exact current preflight. Keep the workflow-level acknowledgement
        // scoped to this click only; the bound authorization remains the
        // authoritative write gate for a conversation plan.
        workflow.ApplyAcknowledged = true;

        try
        {
            if (_viewModel.PlanExecutionRoute ==
                    AssistantPlanExecutionRoute.LabelApply)
            {
                var authorization =
                    _viewModel.CreateApplyAuthorizationFromPlan();

                if (authorization is null)
                {
                    return;
                }

                await _viewModel.ExecuteCurrentPlanAsync(
                    authorization);
                ScrollConversationToEnd();
                return;
            }

            if (!workflow.CanApply)
            {
                return;
            }

            await workflow.ApplyAsync();
            _viewModel.RecordApplyResult();
            ScrollConversationToEnd();
        }
        finally
        {
            workflow.ApplyAcknowledged = false;
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
            var typedCredential =
                AssistantApiTokenBox.SecurePassword;

            if (typedCredential.Length > 0)
            {
                using var credentialToPersist =
                    typedCredential.Copy();

                credentialToPersist.MakeReadOnly();

                _modelConfigurationStore.Save(
                    _viewModel.AssistantBaseUrl,
                    _viewModel.AssistantModel,
                    credentialToPersist);
            }
            else
            {
                _modelConfigurationStore.Save(
                    _viewModel.AssistantBaseUrl,
                    _viewModel.AssistantModel);
            }

            authorizationSecret =
                typedCredential.Length > 0
                    ? typedCredential.Copy()
                    : _modelConfigurationStore.LoadCredential();

            if (authorizationSecret is null ||
                authorizationSecret.Length == 0)
            {
                _viewModel.SetModelCredentialAvailable(
                    false);
                return;
            }

            authorizationSecret.MakeReadOnly();

            _viewModel.SetModelCredentialAvailable(
                true);

            await _viewModel.InterpretAsync(
                authorizationSecret);
        }
        finally
        {
            authorizationSecret?.Dispose();
            ScrollConversationToEnd();
        }
    }

    private void AssistantApiTokenBox_PasswordChanged(
        object sender,
        RoutedEventArgs e)
    {
        _viewModel.SetModelCredentialAvailable(
            AssistantApiTokenBox.SecurePassword.Length > 0 ||
            _modelConfigurationStore.HasStoredCredential());
    }

    private void ClearAssistantToken_Click(
        object sender,
        RoutedEventArgs e)
    {
        _modelConfigurationStore.ClearCredential(
            _viewModel.AssistantBaseUrl,
            _viewModel.AssistantModel);

        AssistantApiTokenBox.Clear();

        _viewModel.SetModelCredentialAvailable(
            false);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.CancelActiveOperation();

        using var typedCredential =
            AssistantApiTokenBox.SecurePassword.Length > 0
                ? AssistantApiTokenBox.SecurePassword.Copy()
                : null;

        typedCredential?.MakeReadOnly();

        _modelConfigurationStore.Save(
            _viewModel.AssistantBaseUrl,
            _viewModel.AssistantModel,
            typedCredential);

        base.OnClosed(e);
    }

    private void ScrollConversationToEnd()
    {
        ConversationScrollViewer.UpdateLayout();
        ConversationScrollViewer.ScrollToEnd();
    }
}
