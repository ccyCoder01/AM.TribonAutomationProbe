using System.Text.Json;
using AM.TribonAutomationProbe.Adapter.FileBridge;
using AM.TribonAutomationProbe.Core;
using AM.TribonAutomationProbe.Protocol;
using Xunit;

namespace AM.TribonAutomationProbe.Tests;

public sealed class Round41AIntegrationClosureTests
{
    [Fact]
    public void ExistingPropertyDriftDoesNotFailSuccessfulMissingLabelBatch()
    {
        var plan = new[]
        {
            Plan("label:LB-01", "LB-01", "LB-01", 110, 250),
            Plan("label:LB-02", "LB-02", "LB-02", 110, 216.5),
            Plan("label:LL-01", "LL-01", "LL-01", 252, 242),
            Plan("label:LL-02", "LL-02", "LL-02", 307, 242)
        };

        var observed = new[]
        {
            Observed("h1", "LB-01", 110, 250, 3.5, "Yellow"),
            Observed("h2", "LB-02", 110, 216.5, 3.5, "Yellow"),
            Observed("h3", "LL-01", 236, 191, 3.0, "Yellow"),
            Observed("h4", "LL-02", 289, 185, 3.0, "Yellow")
        };

        var decision = GeometryLabelPostcheckEvaluator.Evaluate(
            new GeometryLabelPostcheckInput(
                plan,
                observed,
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "label:LL-01",
                    "label:LL-02"
                },
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "label:LB-01",
                    "label:LB-02"
                },
                Array.Empty<string>()));

        Assert.Equal("SUCCESS", decision.Status);
        Assert.Equal(4, decision.PostValidLabelCount);
        Assert.Equal(2, decision.PostCreatedValidCount);
        Assert.Equal(0, decision.PostCreatedPropertyErrorCount);
        Assert.Equal(0, decision.PostExistingMatchErrorCount);
        Assert.Equal(2, decision.PostExistingPropertyDriftCount);
    }

    [Fact]
    public void CreatedLabelPositionMismatchFailsPostcheck()
    {
        var plan = new[]
        {
            Plan("label:LB-01", "LB-01", "LB-01", 110, 250)
        };

        var observed = new[]
        {
            Observed("h1", "LB-01", 111, 250, 3.5, "Yellow")
        };

        var decision = GeometryLabelPostcheckEvaluator.Evaluate(
            new GeometryLabelPostcheckInput(
                plan,
                observed,
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "label:LB-01"
                },
                Array.Empty<string>()));

        Assert.Equal("FAILED_POSTCHECK", decision.Status);
        Assert.Equal(1, decision.PostCreatedPropertyErrorCount);
        Assert.Equal(0, decision.PostCreatedValidCount);
    }

    [Fact]
    public void CreatedLabelHeightMismatchFailsPostcheck()
    {
        var plan = new[]
        {
            Plan("label:LB-01", "LB-01", "LB-01", 110, 250)
        };

        var observed = new[]
        {
            Observed("h1", "LB-01", 110, 250, 3.0, "Yellow")
        };

        var decision = GeometryLabelPostcheckEvaluator.Evaluate(
            new GeometryLabelPostcheckInput(
                plan,
                observed,
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "label:LB-01"
                },
                Array.Empty<string>()));

        Assert.Equal("FAILED_POSTCHECK", decision.Status);
        Assert.Equal(1, decision.PostCreatedPropertyErrorCount);
    }

    [Fact]
    public void PartialCreationFailureRequiresManualRecovery()
    {
        var plan = new[]
        {
            Plan("label:LB-01", "LB-01", "LB-01", 110, 250),
            Plan("label:LB-02", "LB-02", "LB-02", 110, 216.5)
        };

        var observed = new[]
        {
            Observed("h1", "LB-01", 110, 250, 3.5, "Yellow")
        };

        var decision = GeometryLabelPostcheckEvaluator.Evaluate(
            new GeometryLabelPostcheckInput(
                plan,
                observed,
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "label:LB-01",
                    "label:LB-02"
                },
                new[] { "label:LB-02" }));

        Assert.Equal("PARTIAL_FAILURE", decision.Status);
        Assert.True(decision.ManualRecoveryRequired);
        Assert.Equal(1, decision.PostMissingCount);
    }

    [Fact]
    public void DuplicateCreatedLabelFailsPostcheck()
    {
        var plan = new[]
        {
            Plan("label:LB-01", "LB-01", "LB-01", 110, 250)
        };

        var observed = new[]
        {
            Observed("h1", "LB-01", 110, 250, 3.5, "Yellow"),
            Observed("h2", "LB-01", 110, 250, 3.5, "Yellow")
        };

        var decision = GeometryLabelPostcheckEvaluator.Evaluate(
            new GeometryLabelPostcheckInput(
                plan,
                observed,
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "label:LB-01"
                },
                Array.Empty<string>()));

        Assert.Equal("FAILED_POSTCHECK", decision.Status);
        Assert.Equal(1, decision.PostDuplicateCount);
        Assert.Equal(1, decision.PostCreatedPropertyErrorCount);
    }

    [Fact]
    public async Task FileBridgeTransportAcceptsMatchingCorrelationEnvelope()
    {
        var root = CreateTempDirectory();

        try
        {
            var transport = new FileBridgeTransport(
                new FileBridgeOptions(root, PollIntervalMs: 5, DefaultTimeoutMs: 2000));

            var command = Command("geometry.detect");
            var sendTask = transport.SendAsync(command, CancellationToken.None);

            await WaitForRequestAsync(root);
            await WriteResultAsync(
                root,
                command,
                command.CommandId,
                command.CorrelationId,
                command.MessageId,
                new
                {
                    schemaVersion = "1.0",
                    taskType = "geometry.detect",
                    operationId = "op-1"
                });

            var result = await sendTask;
            Assert.Equal(command.CommandId, result.CommandId);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task FileBridgeTransportRejectsCommandIdMismatch()
    {
        var root = CreateTempDirectory();

        try
        {
            var transport = new FileBridgeTransport(
                new FileBridgeOptions(root, PollIntervalMs: 5, DefaultTimeoutMs: 2000));

            var command = Command("geometry.detect");
            var sendTask = transport.SendAsync(command, CancellationToken.None);

            await WaitForRequestAsync(root);
            await WriteResultAsync(
                root,
                command,
                "CMD-WRONG",
                command.CorrelationId,
                command.MessageId,
                new
                {
                    schemaVersion = "1.0",
                    taskType = "geometry.detect",
                    operationId = "op-1"
                });

            var error = await Assert.ThrowsAsync<ProbeException>(
                async () => await sendTask);

            Assert.Equal(ProbeErrorCodes.InvalidResultMessage, error.Code);
            Assert.Contains("commandId", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task GeometryAdapterRejectsOperationIdMismatch()
    {
        var root = CreateTempDirectory();

        try
        {
            var transport = new FileBridgeTransport(
                new FileBridgeOptions(root, PollIntervalMs: 5, DefaultTimeoutMs: 2000));

            var adapter = new FileBridgeGeometryAutomationAdapter(transport);
            var task = adapter.DetectAsync(
                new GeometryDetectionRequest(OperationId: "op-expected"),
                CancellationToken.None);

            var command = await ReadCommandAsync(root);

            await WriteResultAsync(
                root,
                command,
                command.CommandId,
                command.CorrelationId,
                command.MessageId,
                new
                {
                    schemaVersion = "1.0",
                    taskType = "geometry.detect",
                    operationId = "op-wrong",
                    drawingContext = "current_drafting_context",
                    startedAt = DateTimeOffset.UtcNow,
                    completedAt = DateTimeOffset.UtcNow,
                    status = "succeeded",
                    drawingWritePerformed = false,
                    objects = Array.Empty<object>(),
                    diagnostics = new
                    {
                        capturedContourCount = 0,
                        assignedUniqueContourCount = 0,
                        unassignedContourCount = 0,
                        conflictHandleCount = 0,
                        parseFailureCount = 0
                    },
                    savePerformed = false
                });

            var error = await Assert.ThrowsAsync<ProbeException>(
                async () => await task);

            Assert.Equal(ProbeErrorCodes.InvalidResultMessage, error.Code);
            Assert.Contains("operationId", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static GeometryLabelPlanContract Plan(
        string operationId,
        string stableObjectId,
        string text,
        double x,
        double y) =>
        new(
            operationId,
            stableObjectId,
            text,
            x,
            y,
            3.5,
            "Yellow",
            new LayoutRectangle(x - 20, y - 60, x + 20, y - 10),
            100);

    private static GeometryObservedLabelContract Observed(
        string handle,
        string text,
        double x,
        double y,
        double height,
        string colour) =>
        new(
            handle,
            text,
            x,
            y,
            height,
            colour,
            new LayoutRectangle(x, y, x + 10, y + 3));

    private static BridgeCommand Command(string action) =>
        new()
        {
            MessageId = "MSG-" + Guid.NewGuid().ToString("N"),
            CommandId = "CMD-" + Guid.NewGuid().ToString("N"),
            CorrelationId = "COR-" + Guid.NewGuid().ToString("N"),
            Action = action,
            Payload = JsonSerializer.SerializeToElement(
                new { operationId = "op-1" }),
            Execution = new BridgeExecutionOptions(2000)
        };

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "AM.TribonAutomationProbe.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task WaitForRequestAsync(string root)
    {
        var inbox = Path.Combine(root, "inbox");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (Directory.Exists(inbox) &&
                Directory.EnumerateFiles(inbox, "*.request.json").Any())
                return;

            await Task.Delay(5);
        }

        throw new TimeoutException("Bridge request was not created.");
    }

    private static async Task<BridgeCommand> ReadCommandAsync(string root)
    {
        await WaitForRequestAsync(root);
        var path = Directory.EnumerateFiles(
            Path.Combine(root, "inbox"),
            "*.request.json").Single();

        return JsonSerializer.Deserialize<BridgeCommand>(
            await File.ReadAllTextAsync(path),
            JsonDefaults.Options)
            ?? throw new InvalidOperationException("Request JSON was empty.");
    }

    private static async Task WriteResultAsync(
        string root,
        BridgeCommand command,
        string commandId,
        string correlationId,
        string causationId,
        object resultPayload)
    {
        var output = Path.Combine(root, "output");
        Directory.CreateDirectory(output);

        var result = new BridgeResult
        {
            MessageId = "RES-" + command.CommandId,
            CommandId = commandId,
            CorrelationId = correlationId,
            CausationId = causationId,
            Status = "succeeded",
            Result = JsonSerializer.SerializeToElement(resultPayload)
        };

        var path = Path.Combine(
            output,
            command.CommandId + ".result.json");

        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(result, JsonDefaults.Options));
    }
}
