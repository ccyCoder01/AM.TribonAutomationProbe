using AM.TribonAutomationProbe.Core;
using Xunit;

namespace AM.TribonAutomationProbe.Tests;

public sealed class AssistantIntentClassifierTests
{
    private readonly RuleBasedAssistantLanguageModel _model = new();

    [Theory]
    [InlineData("识别当前图纸中的目标对象", AssistantIntent.DetectGeometry)]
    [InlineData("高亮所有吊梁和吊耳", AssistantIntent.HighlightLifting)]
    [InlineData("把所有法兰高亮出来", AssistantIntent.HighlightFlanges)]
    [InlineData("清除当前高亮", AssistantIntent.ClearHighlight)]
    [InlineData("检查有没有缺失标签", AssistantIntent.PreflightLabels)]
    [InlineData("创建缺失的对象标签", AssistantIntent.ApplyMissingLabels)]
    [InlineData("补齐对象标签", AssistantIntent.ApplyMissingLabels)]
    public async Task SupportedChineseInstructionMapsToWhitelistIntent(
        string text,
        AssistantIntent expected)
    {
        var result = await _model.InterpretAsync(
            new AssistantConversationContext(text),
            CancellationToken.None);

        Assert.False(result.ClarificationRequired);
        Assert.Single(result.Tasks);
        Assert.Equal(expected, result.Tasks[0].Intent);
        Assert.InRange(result.Tasks[0].Confidence, 0.9, 1.0);
    }

    [Fact]
    public async Task CompoundInstructionPreservesTaskOrder()
    {
        var result = await _model.InterpretAsync(
            new AssistantConversationContext(
                "先看看图纸里有多少吊梁、吊耳和法兰，然后把所有法兰高亮出来"),
            CancellationToken.None);

        Assert.False(result.ClarificationRequired);
        Assert.Equal(
            [
                AssistantIntent.DetectGeometry,
                AssistantIntent.HighlightFlanges
            ],
            result.Tasks.Select(x => x.Intent).ToArray());
    }

    [Fact]
    public async Task AmbiguousInstructionRequiresClarification()
    {
        var result = await _model.InterpretAsync(
            new AssistantConversationContext("帮我把这张图处理好"),
            CancellationToken.None);

        Assert.True(result.ClarificationRequired);
        Assert.Empty(result.Tasks);
        Assert.Contains("明确", result.ClarificationQuestion!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsupportedInstructionDoesNotCreateTask()
    {
        var result = await _model.InterpretAsync(
            new AssistantConversationContext("把所有管子删除"),
            CancellationToken.None);

        Assert.True(result.ClarificationRequired);
        Assert.Empty(result.Tasks);
    }

    [Fact]
    public async Task PartiallyRecognizedInstructionDoesNotPartiallyExecute()
    {
        var result = await _model.InterpretAsync(
            new AssistantConversationContext(
                "识别当前图纸中的目标对象，然后执行一个自定义脚本"),
            CancellationToken.None);

        Assert.True(result.ClarificationRequired);
        Assert.Single(result.Tasks);
        Assert.Equal(AssistantIntent.DetectGeometry, result.Tasks[0].Intent);
    }
}
