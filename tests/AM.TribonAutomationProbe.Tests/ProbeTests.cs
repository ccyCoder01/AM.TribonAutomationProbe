using AM.TribonAutomationProbe.Adapter.Mock;
using AM.TribonAutomationProbe.Core;
using Xunit;

namespace AM.TribonAutomationProbe.Tests;

public sealed class ProbeTests
{
    [Fact] public void HelpAndCommandsParseSafely() { Assert.True(CliParser.Parse(["--help"]).ShowHelp); Assert.True(CliParser.Parse(["-h"]).ShowHelp); Assert.Contains("must be specified", CliParser.Parse([]).Error); Assert.Equal("context", CliParser.Parse(["context"]).Options!.Command); Assert.Equal("mock", CliParser.Parse(["context"]).Options!.Adapter); }
    [Fact] public void BridgeRootPrecedenceAndNormalization() { var env = CliParser.Parse(["context"], "env root").Options!; Assert.Equal(Path.GetFullPath("env root"), env.BridgeRoot); var cli = CliParser.Parse(["context", "--bridge-root=cli root"], "env root").Options!; Assert.Equal(Path.GetFullPath("cli root"), cli.BridgeRoot); }
    [Theory] [InlineData("0")] [InlineData("-1")] [InlineData("abc")] public void InvalidTimeoutFails(string value) => Assert.NotNull(CliParser.Parse(["context", $"--timeout-ms={value}"]).Error);
    [Fact] public void PositiveOptionsAndWriteRulesParse() { var o = CliParser.Parse(["context", "--adapter=file-bridge", "--timeout-ms=120000", "--poll-interval-ms=10"]).Options!; Assert.Equal(120000, o.TimeoutMs); Assert.Equal(10, o.PollIntervalMs); Assert.False(o.AllowWrite); Assert.False(o.ConfirmWrite); Assert.True(CliParser.Parse(["move-annotation", "--adapter=file-bridge"]).Options!.RequiresWrite); Assert.True(CliParser.Parse(["run-all"]).Options!.RequiresWrite); Assert.True(CliParser.Parse(["apply-missing-object-labels"]).Options!.RequiresWrite); Assert.False(CliParser.Parse(["context", "--adapter=file-bridge"]).Options!.RequiresWrite); }
    [Fact] public void AssistantCommandAndConfirmationOptionsParse() { var result = CliParser.Parse(["assistant", "--text=创建缺失的对象标签", "--confirm-write=true", "--allow-write=true", "--confirmed-preflight-operation-id=PREFLIGHT-1", "--confirmed-plan-hash=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "--confirmed-operation-ids=label:LB-01,label:PF-01"]); Assert.Null(result.Error); Assert.Equal("assistant", result.Options!.Command); Assert.Equal("创建缺失的对象标签", result.Options.Get("text")); Assert.True(result.Options.ConfirmWrite); Assert.True(result.Options.AllowWrite); Assert.Equal("PREFLIGHT-1", result.Options.Get("confirmed-preflight-operation-id")); Assert.Equal("label:LB-01,label:PF-01", result.Options.Get("confirmed-operation-ids")); }
    [Fact] public void AssistantModelOptionsDefaultToRule() { var options = CliParser.Parse(["assistant", "--text=识别对象"]).Options!; Assert.Null(options.AssistantBaseUrl); Assert.Null(options.AssistantModel); }
    [Fact] public void AssistantModelCliOptionsParse() { var options = CliParser.Parse(["assistant-interpret", "--text=高亮法兰", "--base-url=https://example.invalid/v1", "--model=test-model"]).Options!; Assert.Equal("https://example.invalid/v1", options.AssistantBaseUrl); Assert.Equal("test-model", options.AssistantModel); }
    [Fact] public void ApiKeyCannotBePassedOnCommandLine() => Assert.NotNull(CliParser.Parse(["assistant", "--api-key=secret"]).Error);
    [Fact] public void PointToleranceWorks() => Assert.True(new Point2D(1, 2).IsWithinTolerance(new(1.005, 1.995), .01));
    [Fact] public async Task MockMoveRequiresExpectedPosition()
    {
        var adapter = new MockTribonAdapter(); var item = (await adapter.ExportAnnotationsAsync(new(), default)).Annotations[0];
        await Assert.ThrowsAsync<ProbeException>(() => adapter.MoveAnnotationAsync(new() { ObjectRef = item.ObjectRef, ExpectedPosition = new(0, 0), DesiredPosition = new(2, 2) }, default));
    }
    [Fact] public async Task MockRunMovesAndVerifies()
    {
        var adapter = new MockTribonAdapter(); var item = (await adapter.ExportAnnotationsAsync(new(), default)).Annotations[0];
        var result = await adapter.MoveAnnotationAsync(new() { ObjectRef = item.ObjectRef, ExpectedPosition = item.Position, DesiredPosition = new(130, 95) }, default);
        Assert.True(result.WriteSucceeded && result.RefreshSucceeded && result.VerificationSucceeded);
    }
}
