using System.Security.Claims;

namespace SecureTenantAgent.Api.Tenancy;

public static class AppRoles
{
    public const string Member = "Member";
    public const string Reviewer = "Reviewer";
}

/// <summary>
/// Who is calling and for which tenant. Built only from the authenticated principal,
/// never from the request body or from anything the model says.
/// </summary>
public interface ITenantContext
{
    string TenantId { get; }
    string UserId { get; }
    bool IsReviewer { get; }
}

public sealed class HttpTenantContext(IHttpContextAccessor accessor, IConfiguration config) : ITenantContext
{
    private ClaimsPrincipal User =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
            ? user
            : throw new UnauthorizedAccessException("No authenticated user.");

    // With Entra ID this is usually the "tid" claim or an app-specific claim mapped to a tenant.
    private string TenantClaim => config["Tenancy:TenantClaimType"] ?? "tenant_id";

    public string TenantId =>
        User.FindFirstValue(TenantClaim) is { Length: > 0 } tenant
            ? tenant
            : throw new UnauthorizedAccessException("The token has no tenant claim.");

    public string UserId =>
        User.FindFirstValue("oid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("The token has no user id.");

    public bool IsReviewer => User.IsInRole(AppRoles.Reviewer);
}

/// <summary>Fixed context for seeding and background work, where there is no HTTP request.</summary>
public sealed class FixedTenantContext(string tenantId, string userId = "system", bool isReviewer = false) : ITenantContext
{
    public string TenantId { get; } = tenantId;
    public string UserId { get; } = userId;
    public bool IsReviewer { get; } = isReviewer;
}
