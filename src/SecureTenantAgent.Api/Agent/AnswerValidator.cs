namespace SecureTenantAgent.Api.Agent;

/// <summary>
/// Provenance check: the model's output is untrusted until proven otherwise.
/// Every citation must point to a document a tool actually returned to this user in this run,
/// and its quote must really appear in that document.
/// </summary>
public static class AnswerValidator
{
    public static IReadOnlyList<string> Validate(AssistantAnswer? answer, AgentRunContext run)
    {
        if (answer is null) return ["The model did not return a structured answer."];

        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(answer.Answer))
            problems.Add("The answer is empty.");

        foreach (var c in answer.Citations ?? [])
        {
            if (!run.RetrievedBodies.TryGetValue(c.DocumentId, out var body))
            {
                problems.Add($"Citation {c.DocumentId} was not retrieved for this user.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(c.Quote) || !Normalize(body).Contains(Normalize(c.Quote), StringComparison.OrdinalIgnoreCase))
                problems.Add($"The quote for {c.DocumentId} does not appear in the document.");
        }
        return problems;
    }

    private static string Normalize(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
