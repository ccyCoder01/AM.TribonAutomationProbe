using System.IO;
using Xunit;
using AM.TribonAutomationProbe.Core;
using AM.TribonAutomationProbe.Desktop.Models;
using AM.TribonAutomationProbe.Desktop.Services;

namespace AM.TribonAutomationProbe.Desktop.Tests;

public sealed class ConsoleAssistantWorkflowClientTests
{
    [Fact]
    public void BuildInterpretArguments_IsPlanOnlyAndMockBound()
    {
        var arguments = ConsoleAssistantWorkflowClient.BuildInterpretArguments(
            CreateSettings(),
            "检查当前图纸中的对象标签");

        Assert.Contains("assistant-interpret", arguments);
        Assert.Contains("--adapter=mock", arguments);
        Assert.Contains(
            "--text=检查当前图纸中的对象标签",
            arguments);
        Assert.DoesNotContain("--adapter=file-bridge", arguments);
        Assert.DoesNotContain("--allow-write=true", arguments);
        Assert.DoesNotContain("--confirm-write=true", arguments);
        Assert.DoesNotContain(
            arguments,
            argument => argument.Contains(
                "token",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            arguments,
            argument => argument.StartsWith(
                "--base-url=",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            arguments,
            argument => argument.StartsWith(
                "--model=",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateInterpretation_AcceptsConfirmationGatedWritePlan()
    {
        ConsoleAssistantWorkflowClient.ValidateInterpretation(
            CreateEnvelope(AssistantIntent.ApplyMissingLabels),
            "创建缺失对象标签");
    }

    [Fact]
    public void ValidateInterpretation_RejectsReportedExecution()
    {
        var invalid = CreateEnvelope(
            AssistantIntent.PreflightLabels) with
        {
            ExecutionPerformed = true
        };

        Assert.Throws<InvalidDataException>(
            () => ConsoleAssistantWorkflowClient.ValidateInterpretation(
                invalid,
                "检查对象标签"));
    }

    [Fact]
    public void ValidateInterpretation_RejectsRiskMismatch()
    {
        var valid = CreateEnvelope(AssistantIntent.ApplyMissingLabels);
        var invalidTask = valid.Plan.Tasks[0] with
        {
            Risk = AssistantTaskRisk.ReadOnly,
            RequiresConfirmation = false
        };
        var invalid = valid with
        {
            Plan = valid.Plan with
            {
                Tasks = new[] { invalidTask },
                ContainsWrite = false,
                RequiresConfirmation = false,
                State = AssistantTaskState.Planned
            }
        };

        Assert.Throws<InvalidDataException>(
            () => ConsoleAssistantWorkflowClient.ValidateInterpretation(
                invalid,
                "创建缺失对象标签"));
    }

    [Fact]
    public void BuildAuthorizationValue_AddsBearerPrefix()
    {
        var value = ConsoleAssistantWorkflowClient.BuildAuthorizationValue(
            AssistantAuthorizationMode.BearerToken,
            "token-value");

        Assert.Equal("Bearer token-value", value);
    }

    [Fact]
    public void BuildAuthorizationValue_RejectsExistingBearerPrefix()
    {
        Assert.Throws<ArgumentException>(
            () => ConsoleAssistantWorkflowClient.BuildAuthorizationValue(
                AssistantAuthorizationMode.BearerToken,
                "Bearer token-value"));
    }

    [Fact]
    public void ProviderSettings_RejectsNonHttpsBaseUrl()
    {
        var settings = new AssistantProviderSessionSettings(
            AssistantProviderMode.OpenAiCompatible,
            "http://example.test/chat/completions",
            "model",
            AssistantAuthorizationMode.BearerToken);

        Assert.Throws<ArgumentException>(settings.Validate);
    }

    [Fact]
    public void ValidateInterpretation_RejectsRuleEnvelopeForRealProvider()
    {
        var provider = new AssistantProviderSessionSettings(
            AssistantProviderMode.OpenAiCompatible,
            "https://example.test/chat/completions",
            "model",
            AssistantAuthorizationMode.BearerToken);

        Assert.Throws<InvalidDataException>(
            () => ConsoleAssistantWorkflowClient.ValidateInterpretation(
                CreateEnvelope(AssistantIntent.PreflightLabels),
                "检查对象标签",
                provider));
    }

    internal static AssistantInterpretationEnvelope CreateEnvelope(
        AssistantIntent intent)
    {
        var userText = intent == AssistantIntent.ApplyMissingLabels
            ? "创建缺失对象标签"
            : intent == AssistantIntent.PreflightLabels
                ? "检查对象标签"
                : "高亮法兰";
        var isWrite = intent == AssistantIntent.ApplyMissingLabels;
        var taskType = intent switch
        {
            AssistantIntent.PreflightLabels => "geometry.label-preflight",
            AssistantIntent.ApplyMissingLabels => "geometry.label-apply-missing",
            AssistantIntent.HighlightFlanges => "geometry.highlight-flanges",
            _ => throw new ArgumentOutOfRangeException(nameof(intent))
        };
        var interpreted = new AssistantInterpretedTask(
            intent,
            0.98,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["scope"] = "current_drafting_context"
            });
        var interpretation = new AssistantInterpretation(
            "deterministic",
            "rule-based-v1",
            new[] { interpreted },
            false,
            Explanation: "test");
        var task = new AssistantPlannedTask(
            1,
            intent,
            taskType,
            isWrite
                ? AssistantTaskRisk.DrawingWrite
                : AssistantTaskRisk.ReadOnly,
            isWrite,
            false,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["scope"] = "current_drafting_context"
            });
        var plan = new AssistantTaskPlan(
            "1.0",
            "PLAN-TEST",
            userText,
            DateTimeOffset.UtcNow,
            new[] { task },
            isWrite,
            isWrite,
            false,
            isWrite
                ? AssistantTaskState.AwaitingConfirmation
                : AssistantTaskState.Planned,
            isWrite
                ? "write confirmation required"
                : "read only");

        return new AssistantInterpretationEnvelope(
            "1.0",
            AssistantTaskOrchestrator.ProductName,
            interpretation,
            plan,
            false,
            false,
            false);
    }

    private static ConsoleWorkflowSettings CreateSettings() =>
        new(
            @"C:\Package\AM.TribonAutomationProbe.Console.exe",
            @"C:\AM_TribonBridge",
            600000,
            200);
}
