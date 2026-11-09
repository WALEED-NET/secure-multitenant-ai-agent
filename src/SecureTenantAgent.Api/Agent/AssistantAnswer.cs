using System.ComponentModel;

namespace SecureTenantAgent.Api.Agent;

/// <summary>The only shape the agent is allowed to answer in (JSON schema structured output).</summary>
public sealed record AssistantAnswer(
    [property: Description("The answer for the user, based only on retrieved documents.")] string Answer,
    [property: Description("Every document the answer relies on, with a short supporting quote.")] IReadOnlyList<Citation> Citations,
    [property: Description("True when the answer is uncertain or a person should check it before acting.")] bool NeedsHumanReview);

public sealed record Citation(
    [property: Description("Id of a document returned by a tool in this conversation.")] Guid DocumentId,
    [property: Description("A short quote copied exactly from that document.")] string Quote);

public sealed record AskRequest(string Question);

public sealed record AskResponse(AssistantAnswer Answer, IReadOnlyList<Guid> RetrievedDocumentIds);
