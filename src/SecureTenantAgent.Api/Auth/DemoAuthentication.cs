using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SecureTenantAgent.Api.Tenancy;

namespace SecureTenantAgent.Api.Auth;

/// <summary>
/// Local-only sign-in: pick a seeded demo user with the <c>X-Demo-User</c> header.
/// It produces the same claims an Entra ID token would, so the rest of the app cannot
/// tell the difference. Registered only when <c>Auth:DemoUsers</c> is enabled.
/// </summary>
public sealed class DemoAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Demo";
    public const string Header = "X-Demo-User";

    public static readonly IReadOnlyDictionary<string, DemoUser> Users = new Dictionary<string, DemoUser>
    {
        ["alice"] = new("alice", "contoso-clinic", AppRoles.Member),
        ["omar"] = new("omar", "contoso-clinic", AppRoles.Reviewer),
        ["bob"] = new("bob", "fabrikam-legal", AppRoles.Member),
        ["lina"] = new("lina", "fabrikam-legal", AppRoles.Reviewer),
    };

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var name))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!Users.TryGetValue(name.ToString(), out var user))
            return Task.FromResult(AuthenticateResult.Fail("Unknown demo user."));

        var identity = new ClaimsIdentity(
        [
            new Claim("oid", user.Id),
            new Claim("tenant_id", user.TenantId),
            new Claim(ClaimTypes.Role, user.Role),
        ], SchemeName, ClaimTypes.NameIdentifier, ClaimTypes.Role);

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

public sealed record DemoUser(string Id, string TenantId, string Role);
