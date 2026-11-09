using System.Text.Json;
using Microsoft.Extensions.AI;

namespace SecureTenantAgent.Tests.Infrastructure;

/// <summary>
/// A deterministic stand-in for the model. Each test scripts what the "model" does:
/// which tools it calls, and what it finally answers. That lets us play a malicious or
/// confused model on purpose and prove the application still holds.
/// </summary>
public sealed class ScriptedChatClient : IChatClient
{
    public Func<ScriptTurn, ChatResponse> Script { get; set; } = _ => Answer("No script set.", []);

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
    {
        var list = messages.ToList();
        var toolResults = list.SelectMany(m => m.Contents).OfType<FunctionResultContent>().ToList();
        var question = list.Last(m => m.Role == ChatRole.User).Text;
        return Task.FromResult(Script(new ScriptTurn(question, toolResults, options)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }

    public static ChatResponse CallTools(params (string Name, Dictionary<string, object?> Args)[] calls) =>
        new(new ChatMessage(ChatRole.Assistant,
            calls.Select(c => (AIContent)new FunctionCallContent(Guid.NewGuid().ToString("N"), c.Name, c.Args)).ToList()));

    public static ChatResponse Answer(string text, IEnumerable<(Guid Id, string Quote)> citations, bool needsReview = false) =>
        new(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(new
        {
            answer = text,
            citations = citations.Select(c => new { documentId = c.Id, quote = c.Quote }),
            needsHumanReview = needsReview,
        })));
}

public sealed record ScriptTurn(string Question, IReadOnlyList<FunctionResultContent> ToolResults, ChatOptions? Options)
{
    public bool FirstTurn => ToolResults.Count == 0;
    public string AllToolOutput => string.Join("\n", ToolResults.Select(r => r.Result?.ToString()));
}
