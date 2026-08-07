using System.IO;
using Xunit;
using AM.TribonAutomationProbe.Core;
using AM.TribonAutomationProbe.Desktop.Models;
using AM.TribonAutomationProbe.Desktop.Services;

namespace AM.TribonAutomationProbe.Desktop.Tests;

public sealed class ConsoleAssistantReadOnlyPlanExecutionClientTests
{
    [Fact]
    public void BuildExecutionArguments_MapsAllSupportedIntents()
    {
        var expected = new Dictionary<AssistantIntent, string>
        {
            [AssistantIntent.DetectGeometry] = "detect-geometry",
            [AssistantIntent.HighlightLifting] = "highlight-lifting",
            [AssistantIntent.HighlightFlanges] = "highlight-flanges",
            [AssistantIntent.ClearHighlight] = "clear-highlight"
        };

        foreach (var pair in expected)
        {
            var arguments =
                ConsoleAssistantReadOnlyPlanExecutionClient
                    .BuildExecutionArguments(
                        CreateSettings(),
                        CreatePlan(pair.Key));

            Assert.Equal(pair.Value, arguments[0]);
            Assert.Contains("--adapter=file-bridge", arguments);
        }
    }

    [Fact]
    public void BuildExecutionArguments_ContainsNoModelOrWriteArguments()
    {
        var arguments =
            ConsoleAssistantReadOnlyPlanExecutionClient
                .BuildExecutionArguments(
                    CreateSettings(),
                    CreatePlan(AssistantIntent.HighlightFlanges));

        Assert.DoesNotContain(
            arguments,
            value => value.StartsWith(
                "assistant-",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("--allow-write=true", arguments);
        Assert.DoesNotContain("--confirm-write=true", arguments);
        Assert.DoesNotContain(
            arguments,
            value => value.Contains(
                "model",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            arguments,
            value => value.Contains(
                "token",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidatePlan_RejectsWritePlan()
    {
        var invalid = CreatePlan(
            AssistantIntent.ApplyMissingLabels,
            risk: AssistantTaskRisk.DrawingWrite,
            requiresConfirmation: true,
            containsWrite: true,
            planRequiresConfirmation: true,
            state: AssistantTaskState.AwaitingConfirmation);

        Assert.Throws<InvalidDataException>(
            () => ConsoleAssistantReadOnlyPlanExecutionClient
                .ValidatePlan(invalid));
    }

    [Fact]
    public void ValidatePlan_RejectsMultipleTasks()
    {
        var first = CreatePlan(AssistantIntent.DetectGeometry);
        var secondTask = first.Tasks[0] with
        {
            Sequence = 2,
            Intent = AssistantIntent.ClearHighlight,
            TaskType = "geometry.highlight-clear"
        };
        var invalid = first with
        {
            Tasks = new[]
            {
                first.Tasks[0],
                secondTask
            }
        };

        Assert.Throws<InvalidDataException>(
            () => ConsoleAssistantReadOnlyPlanExecutionClient
                .ValidatePlan(invalid));
    }

    [Fact]
    public void ValidatePlan_RejectsNonPlannedState()
    {
        var invalid = CreatePlan(
            AssistantIntent.DetectGeometry) with
        {
            State = AssistantTaskState.Completed
        };

        Assert.Throws<InvalidDataException>(
            () => ConsoleAssistantReadOnlyPlanExecutionClient
                .ValidatePlan(invalid));
    }

    [Fact]
    public void ValidateDetectionResult_AcceptsAndRejectsSave()
    {
        var valid = CreateDetectionResult();

        ConsoleAssistantReadOnlyPlanExecutionClient
            .ValidateDetectionResult(valid);

        Assert.Throws<InvalidDataException>(
            () => ConsoleAssistantReadOnlyPlanExecutionClient
                .ValidateDetectionResult(
                    valid with
                    {
                        SavePerformed = true
                    }));
    }

    [Fact]
    public void ValidateHighlightResult_AcceptsAndRejectsCountMismatch()
    {
        var valid = CreateHighlightResult();

        ConsoleAssistantReadOnlyPlanExecutionClient
            .ValidateHighlightResult(
                valid,
                AssistantIntent.HighlightFlanges);

        Assert.Throws<InvalidDataException>(
            () => ConsoleAssistantReadOnlyPlanExecutionClient
                .ValidateHighlightResult(
                    valid with
                    {
                        HighlightSuccessCount = 1
                    },
                    AssistantIntent.HighlightFlanges));
    }

    [Fact]
    public void ValidateClearResult_AcceptsAndRejectsUncleared()
    {
        var valid = new GeometryHighlightClearResult(
            "1.0",
            "geometry.highlight-clear",
            "operation-clear",
            "current_drafting_context",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "succeeded",
            true,
            false,
            false);

        ConsoleAssistantReadOnlyPlanExecutionClient
            .ValidateClearResult(valid);

        Assert.Throws<InvalidDataException>(
            () => ConsoleAssistantReadOnlyPlanExecutionClient
                .ValidateClearResult(
                    valid with
                    {
                        Cleared = false
                    }));
    }

    internal static AssistantTaskPlan CreatePlan(
        AssistantIntent intent,
        AssistantTaskRisk risk = AssistantTaskRisk.ReadOnly,
        bool requiresConfirmation = false,
        bool containsWrite = false,
        bool planRequiresConfirmation = false,
        AssistantTaskState state = AssistantTaskState.Planned)
    {
        var taskType = intent switch
        {
            AssistantIntent.DetectGeometry => "geometry.detect",
            AssistantIntent.HighlightLifting => "geometry.highlight-lifting",
            AssistantIntent.HighlightFlanges => "geometry.highlight-flanges",
            AssistantIntent.ClearHighlight => "geometry.highlight-clear",
            AssistantIntent.ApplyMissingLabels => "geometry.label-apply-missing",
            _ => throw new ArgumentOutOfRangeException(nameof(intent))
        };
        var task = new AssistantPlannedTask(
            1,
            intent,
            taskType,
            risk,
            requiresConfirmation,
            false,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["scope"] = "current_drafting_context"
            });

        return new AssistantTaskPlan(
            "1.0",
            "PLAN-READONLY-TEST",
            "test",
            DateTimeOffset.UtcNow,
            new[] { task },
            planRequiresConfirmation,
            containsWrite,
            false,
            state,
            "test");
    }

    private static GeometryDetectionResult CreateDetectionResult() =>
        new(
            "1.0",
            "geometry.detect",
            "operation-detect",
            "current_drafting_context",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "succeeded",
            false,
            Array.Empty<DetectedGeometryObject>(),
            new GeometryObjectDetectionDiagnostics(
                CapturedContourCount: 12,
                AssignedUniqueContourCount: 12),
            false);

    private static GeometryHighlightResult CreateHighlightResult() =>
        new(
            "1.0",
            "geometry.highlight-flanges",
            "operation-highlight",
            "current_drafting_context",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "succeeded",
            false,
            HighlightedObjectCount: 2,
            HighlightedHandleCount: 4,
            HighlightSuccessCount: 4,
            MissingHandleCount: 0,
            HighlightFailureCount: 0,
            Categories:
            new[]
            {
                GeometryObjectCategory.PIPE_FLANGE_FRONT,
                GeometryObjectCategory.PIPE_FLANGE_SIDE,
                GeometryObjectCategory.STRUCTURAL_FLANGE
            },
            SavePerformed: false);

    private static ConsoleWorkflowSettings CreateSettings() =>
        new(
            @"C:\Package\AM.TribonAutomationProbe.Console.exe",
            @"C:\AM_TribonBridge",
            600000,
            200);
}
