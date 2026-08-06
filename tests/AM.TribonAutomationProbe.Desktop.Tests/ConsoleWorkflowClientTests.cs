using Xunit;
using AM.TribonAutomationProbe.Core;
using AM.TribonAutomationProbe.Desktop.Models;
using AM.TribonAutomationProbe.Desktop.Services;

namespace AM.TribonAutomationProbe.Desktop.Tests;

public sealed class ConsoleWorkflowClientTests
{
    [Fact]
    public void BuildPreflightArguments_IsReadOnly()
    {
        var settings = CreateSettings();

        var arguments =
            ConsoleWorkflowClient.BuildPreflightArguments(
                settings);

        Assert.Contains(
            "preflight-object-labels",
            arguments);
        Assert.Contains(
            "--adapter=file-bridge",
            arguments);
        Assert.DoesNotContain(
            "--allow-write=true",
            arguments);
        Assert.DoesNotContain(
            "--confirm-write=true",
            arguments);
    }

    [Fact]
    public void BuildApplyArguments_UsesExactPreflightBinding()
    {
        var settings = CreateSettings();
        var preflight = CreatePreflight();

        var arguments =
            ConsoleWorkflowClient.BuildApplyArguments(
                settings,
                preflight);

        Assert.Contains(
            "--allow-write=true",
            arguments);
        Assert.Contains(
            "--confirm-write=true",
            arguments);
        Assert.Contains(
            $"--confirmed-preflight-operation-id={preflight.OperationId}",
            arguments);
        Assert.Contains(
            $"--confirmed-plan-hash={preflight.PlanHash}",
            arguments);
        Assert.Contains(
            "--confirmed-operation-ids=label:PF-01,label:SF-01",
            arguments);
    }

    [Fact]
    public void ValidatePreflightResult_RejectsWrite()
    {
        var invalid = CreatePreflight() with
        {
            DrawingWritePerformed = true
        };

        var error = Assert.Throws<InvalidDataException>(
            () =>
                ConsoleWorkflowClient.ValidatePreflightResult(
                    invalid));

        Assert.Contains(
            "drawing write",
            error.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatePreflightResult_RejectsReadySetMismatch()
    {
        var invalid = CreatePreflight() with
        {
            ReadyOperationIds =
                new[]
                {
                    "label:PF-01"
                }
        };

        Assert.Throws<InvalidDataException>(
            () =>
                ConsoleWorkflowClient.ValidatePreflightResult(
                    invalid));
    }

    [Fact]
    public void ValidateApplyResult_RejectsSave()
    {
        var preflight = CreatePreflight();
        var invalid = CreateApply() with
        {
            SavePerformed = true
        };

        var error = Assert.Throws<InvalidDataException>(
            () =>
                ConsoleWorkflowClient.ValidateApplyResult(
                    invalid,
                    preflight));

        Assert.Contains(
            "SAVEWORK",
            error.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateApplyResult_AcceptsExactReceipt()
    {
        ConsoleWorkflowClient.ValidateApplyResult(
            CreateApply(),
            CreatePreflight());
    }

    private static ConsoleWorkflowSettings CreateSettings() =>
        new(
            @"C:\Package\AM.TribonAutomationProbe.Console.exe",
            @"C:\AM_TribonBridge",
            600000,
            200);

    internal static GeometryLabelPreflightResult
        CreatePreflight() =>
        new(
            SchemaVersion: "1.0",
            TaskType: "geometry.label-preflight",
            OperationId: "preflight-operation",
            DrawingContext: "current_drawing_contours",
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow,
            Status: "SUCCESS",
            PreAlreadyPresentCount: 1,
            PreMissingCount: 2,
            PreDuplicateTextCount: 0,
            PreInspectionErrorCount: 0,
            Items:
            new[]
            {
                new GeometryLabelPreflightItem(
                    "label:EXISTING",
                    "source-existing",
                    "EXISTING",
                    "EXISTING",
                    1,
                    0,
                    0,
                    "ALREADY_APPLIED",
                    "HANDLE-1"),
                new GeometryLabelPreflightItem(
                    "label:PF-01",
                    "source-pf",
                    "PF-01",
                    "PF-01",
                    0,
                    0,
                    0,
                    "READY_TO_CREATE"),
                new GeometryLabelPreflightItem(
                    "label:SF-01",
                    "source-sf",
                    "SF-01",
                    "SF-01",
                    0,
                    0,
                    0,
                    "READY_TO_CREATE")
            },
            DrawingWritePerformed: false,
            SavePerformed: false,
            PreTextConflictCount: 0,
            PlanHash:
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            ReadyOperationIds:
            new[]
            {
                "label:PF-01",
                "label:SF-01"
            });

    internal static GeometryLabelApplyMissingResult
        CreateApply() =>
        new(
            SchemaVersion: "1.0",
            TaskType: "geometry.label-apply-missing",
            OperationId: "apply-operation",
            DrawingContext: "current_drawing_contours",
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow,
            Status: "SUCCESS",
            CreatedCount: 2,
            CreateFailedCount: 0,
            PostValidLabelCount: 3,
            PostMissingCount: 0,
            PostDuplicateCount: 0,
            PostCreatedValidCount: 2,
            PostCreatedPropertyErrorCount: 0,
            PostExistingMatchErrorCount: 0,
            PostExistingPropertyDriftCount: 0,
            PostInspectionErrorCount: 0,
            DrawingWritePerformed: true,
            DrawingWriteCount: 2,
            ManualRecoveryRequired: false,
            CreatedRuntimeHandles:
            new[]
            {
                "HANDLE-2",
                "HANDLE-3"
            },
            FailedOperationIds:
                Array.Empty<string>(),
            SavePerformed: false,
            PreAlreadyPresentCount: 1,
            PreMissingCount: 2,
            PreDuplicateTextCount: 0,
            PreInspectionErrorCount: 0,
            CreatedOperationIds:
            new[]
            {
                "label:PF-01",
                "label:SF-01"
            },
            ExistingPropertyDrifts:
                Array.Empty<GeometryLabelPropertyDrift>());
}
