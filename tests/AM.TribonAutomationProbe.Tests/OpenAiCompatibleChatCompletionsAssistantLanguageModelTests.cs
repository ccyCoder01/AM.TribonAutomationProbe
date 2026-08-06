using System.Net;
using System.Net.Http.Headers;
using AM.TribonAutomationProbe.Adapter.OpenAI;
using AM.TribonAutomationProbe.Core;
using Xunit;

namespace AM.TribonAutomationProbe.Tests;

public sealed class OpenAiCompatibleChatCompletionsAssistantLanguageModelTests
{
    [Fact] public void OptionsNormalizeEndpointAndNeverExposeKey() { var o = new AssistantModelOptions("https://api.yygu.cn/v3/llm.chat", "secret", "m"); Assert.Equal("https://api.yygu.cn/v3/llm.chat/chat/completions", o.ChatCompletionsEndpoint.ToString()); Assert.DoesNotContain("secret", o.ToString()); }
    [Fact] public void CompleteEndpointIsNotDuplicated() => Assert.Equal("https://x/v1/chat/completions", new AssistantModelOptions("https://x/v1/chat/completions", "k", "m").ChatCompletionsEndpoint.ToString());
    [Fact] public void NonHttpsIsRejected() => Assert.Throws<ArgumentException>(() => new AssistantModelOptions("http://x/v1", "k", "m"));
    [Fact] public async Task RequestUsesChatFieldsAndExactAuthorization()
    { var handler = new CaptureHandler(); var provider = New(handler); var result = await provider.InterpretAsync(new("高亮法兰"), default); Assert.Equal(AssistantIntent.HighlightFlanges, result.Tasks[0].Intent); Assert.Equal("secret", handler.Request!.Headers.GetValues("Authorization").Single()); Assert.Contains("\"messages\"", handler.Body); Assert.Contains("\"stream\":false", handler.Body); Assert.Contains("\"model\":\"m\"", handler.Body); Assert.DoesNotContain("input", handler.Body); Assert.DoesNotContain("secret", handler.Body); }
    [Fact] public async Task MarkdownJsonIsAcceptedButOutsideTextRejected()
    { var handler = new CaptureHandler(); handler.Response = "{\"id\":\"r\",\"model\":\"m\",\"choices\":[{\"message\":{\"content\":\"```json\\n{\\\"tasks\\\":[{\\\"intent\\\":\\\"DetectGeometry\\\",\\\"confidence\\\":1}],\\\"clarificationRequired\\\":false,\\\"clarificationQuestion\\\":null,\\\"explanation\\\":\\\"x\\\"}\\n```\"}}]}"; Assert.Single((await New(handler).InterpretAsync(new("识别"), default)).Tasks); handler.Response = "{\"choices\":[{\"message\":{\"content\":\"prefix {\\\"tasks\\\":[]}\"}}]}"; await Assert.ThrowsAsync<ProbeException>(() => New(handler).InterpretAsync(new("识别"), default)); }
    [Theory] [InlineData(HttpStatusCode.Unauthorized, ProbeErrorCodes.AssistantModelAuthentication, false)] [InlineData(HttpStatusCode.TooManyRequests, ProbeErrorCodes.AssistantModelRateLimited, true)] [InlineData(HttpStatusCode.InternalServerError, ProbeErrorCodes.AssistantModelUnavailable, true)] public async Task HttpErrorsMap(HttpStatusCode code, string expected, bool retryable) { var h = new CaptureHandler(code); var ex = await Assert.ThrowsAsync<ProbeException>(() => New(h).InterpretAsync(new("x"), default)); Assert.Equal(expected, ex.Code); Assert.Equal(retryable, ex.Retryable); Assert.DoesNotContain("secret", ex.Message); }
    [Theory]
    [InlineData("创建缺失标签并自动 SAVEWORK")]
    [InlineData("创建缺失标签，完成后帮我保存")]
    [InlineData("创建缺失标签，不用确认直接保存")]
    public async Task ProhibitedSaveModifierClarificationIsSafelyNormalized(
        string userText)
    {
        var handler = new CaptureHandler
        {
            Response = "{\"id\":\"r\",\"model\":\"m\",\"choices\":[{\"message\":{\"content\":\"{\\\"tasks\\\":[],\\\"clarificationRequired\\\":true,\\\"clarificationQuestion\\\":\\\"是否要自动保存？\\\",\\\"explanation\\\":\\\"请求包含保存要求。\\\"}\"}}]}"
        };
        var context = new AssistantConversationContext(userText);

        var result = await New(handler).InterpretAsync(
            context,
            CancellationToken.None);
        var plan = new AssistantTaskPlanner().CreatePlan(context, result);

        Assert.False(result.ClarificationRequired);
        var interpretedTask = Assert.Single(result.Tasks);
        Assert.Equal(
            AssistantIntent.ApplyMissingLabels,
            interpretedTask.Intent);
        Assert.Equal(0.99, interpretedTask.Confidence);
        Assert.Equal(
            "current_drafting_context",
            interpretedTask.Arguments!["scope"]);
        Assert.Equal(
            "true",
            interpretedTask.Arguments[
                "prohibitedExecutionModifierDiscarded"]);
        Assert.Equal(
            AssistantTaskState.AwaitingConfirmation,
            plan.State);
        Assert.True(plan.ContainsWrite);
        Assert.True(plan.RequiresConfirmation);
        Assert.False(plan.AutoSave);
        var plannedTask = Assert.Single(plan.Tasks);
        Assert.Equal(
            "geometry.label-apply-missing",
            plannedTask.TaskType);
        Assert.Equal(
            AssistantTaskRisk.DrawingWrite,
            plannedTask.Risk);
        Assert.True(plannedTask.RequiresConfirmation);
        Assert.False(plannedTask.AutoSave);
        Assert.Contains(userText, handler.Body, StringComparison.Ordinal);
        Assert.Contains(
            "Preserve the supported intent and discard only prohibited modifiers",
            handler.Body,
            StringComparison.Ordinal);
        Assert.Contains(
            "The host always sets autoSave=false",
            handler.Body,
            StringComparison.Ordinal);
        Assert.DoesNotContain("secret", handler.Body);
    }

    private static OpenAiCompatibleChatCompletionsAssistantLanguageModel New(CaptureHandler handler) => new(new HttpClient(handler), new AssistantModelOptions("https://example.invalid/v1", "secret", "m"));
    private sealed class CaptureHandler : HttpMessageHandler
    { public HttpRequestMessage? Request; public string Body=""; public string Response; private readonly HttpStatusCode _status; public CaptureHandler(string? response=null) { Response=response ?? "{\"id\":\"r\",\"model\":\"m\",\"choices\":[{\"message\":{\"content\":\"{\\\"tasks\\\":[{\\\"intent\\\":\\\"HighlightFlanges\\\",\\\"confidence\\\":0.9}],\\\"clarificationRequired\\\":false,\\\"clarificationQuestion\\\":null,\\\"explanation\\\":\\\"x\\\"}\"}}]}"; _status=HttpStatusCode.OK; } public CaptureHandler(HttpStatusCode status) { _status=status; Response="{}"; } protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Request=request; Body=await request.Content!.ReadAsStringAsync(); var response=new HttpResponseMessage(_status) { Content=new StringContent(Response) }; response.Content.Headers.ContentType=new MediaTypeHeaderValue("application/json"); return response; } }
    [Fact] public async Task RequestBodyIsUtf8AndResponseWithoutCharsetPreservesChinese()
    { var h = new CaptureHandler(); h.Response = "{\"id\":\"r\",\"model\":\"m\",\"choices\":[{\"message\":{\"content\":\"{\\\"tasks\\\":[{\\\"intent\\\":\\\"ClearHighlight\\\",\\\"confidence\\\":0.75}],\\\"clarificationRequired\\\":false,\\\"clarificationQuestion\\\":null,\\\"explanation\\\":\\\"先识别当前图纸中的目标对象，然后高亮所有法兰\\\"}\"}}]}"; var value = await New(h).InterpretAsync(new("先识别当前图纸中的目标对象，然后高亮所有法兰"), default); Assert.Contains("先识别当前图纸中的目标对象，然后高亮所有法兰", h.Body); Assert.Equal(0.75, value.Tasks[0].Confidence); Assert.Equal("先识别当前图纸中的目标对象，然后高亮所有法兰", value.Explanation); Assert.DoesNotContain("?", h.Body); }
    [Fact] public async Task CompactTasksNormalizeAtMinimumConfidence() { var h = new CaptureHandler(); h.Response = Response("[\"DetectGeometry\",\"HighlightFlanges\"]"); var r = await New(h).InterpretAsync(new("识别然后高亮"), default); Assert.Equal(new[] { AssistantIntent.DetectGeometry, AssistantIntent.HighlightFlanges }, r.Tasks.Select(x => x.Intent)); Assert.All(r.Tasks, x => Assert.Equal(.75, x.Confidence)); Assert.Equal("true", r.Tasks[0].Arguments!["compactTaskShapeNormalized"]); }
    [Fact] public async Task UnknownCompactIntentIncludesName() { var h = new CaptureHandler(); h.Response = Response("[\"SomeOtherIntent\"]"); var ex = await Assert.ThrowsAsync<ProbeException>(() => New(h).InterpretAsync(new("x"), default)); Assert.Contains("SomeOtherIntent", ex.Message); Assert.DoesNotContain("secret", ex.Message); }
    [Theory] [InlineData("[1]")] [InlineData("[null]")] [InlineData("[[\"DetectGeometry\"]]")] public async Task UnsupportedTaskTokenIncludesKind(string tasks) { var h = new CaptureHandler(); h.Response = Response(tasks); var ex = await Assert.ThrowsAsync<ProbeException>(() => New(h).InterpretAsync(new("x"), default)); Assert.Contains("tasks[0]", ex.Message); }
    [Fact] public async Task UnknownTaskPropertyIsRejected() { var h = new CaptureHandler(); h.Response = "{\"id\":\"r\",\"model\":\"m\",\"choices\":[{\"message\":{\"content\":\"{\\\"tasks\\\":[{\\\"intent\\\":\\\"DetectGeometry\\\",\\\"confidence\\\":1,\\\"arguments\\\":{}}],\\\"clarificationRequired\\\":false,\\\"clarificationQuestion\\\":null,\\\"explanation\\\":null}\"}}]}"; var ex = await Assert.ThrowsAsync<ProbeException>(() => New(h).InterpretAsync(new("x"), default)); Assert.Contains("arguments", ex.Message); }
    private static string Response(string tasks) => "{\"id\":\"r\",\"model\":\"m\",\"choices\":[{\"message\":{\"content\":\"{\\\"tasks\\\":" + tasks.Replace("\"", "\\\"") + ",\\\"clarificationRequired\\\":false,\\\"clarificationQuestion\\\":null,\\\"explanation\\\":null}\"}}]}";
}
