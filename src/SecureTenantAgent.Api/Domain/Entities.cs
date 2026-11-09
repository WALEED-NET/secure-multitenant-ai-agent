namespace SecureTenantAgent.Api.Domain;

/// <summary>Marks data that belongs to exactly one tenant. The DbContext filters it automatically.</summary>
public interface ITenantOwned
{
    string TenantId { get; }
}

public sealed class Tenant
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}

/// <summary>A knowledge-base document the assistant can retrieve and cite.</summary>
public sealed class KnowledgeDocument : ITenantOwned
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string TenantId { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }

    /// <summary>Documents marked restricted are only visible to reviewers.</summary>
    public bool Restricted { get; init; }
}

public enum ReviewStatus { Pending, Approved, Rejected }

/// <summary>
/// An action the agent proposed. The agent can never execute it: a human reviewer
/// of the same tenant has to approve it first.
/// </summary>
public sealed class PendingAction : ITenantOwned
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string TenantId { get; init; }
    public required string Kind { get; init; }
    public required Guid DocumentId { get; init; }
    public required string Reason { get; init; }
    public required string RequestedBy { get; init; }
    public DateTime RequestedAtUtc { get; init; } = DateTime.UtcNow;
    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
    public string? ReviewedBy { get; set; }
}

/// <summary>Append-only record of every tool call the agent made, for audit.</summary>
public sealed class AgentAuditEntry : ITenantOwned
{
    public long Id { get; init; }
    public required string TenantId { get; init; }
    public required string UserId { get; init; }
    public required string Tool { get; init; }
    public required string Arguments { get; init; }
    public required string Outcome { get; init; }
    public DateTime AtUtc { get; init; } = DateTime.UtcNow;
}
