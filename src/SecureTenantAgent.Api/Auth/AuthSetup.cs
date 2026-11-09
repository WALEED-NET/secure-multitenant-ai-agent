using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using SecureTenantAgent.Api.Tenancy;

namespace SecureTenantAgent.Api.Auth;

public static class AuthSetup
{
    public const string ReviewerPolicy = "Reviewer";

    /// <summary>
    /// Bearer tokens go to Entra ID (JWT). Requests without a bearer token fall back to the
    /// demo scheme, which only exists when explicitly enabled for local runs and tests.
    /// </summary>
    public static IServiceCollection AddAppAuthentication(this IServiceCollection services, IConfiguration config)
    {
        var demoEnabled = config.GetValue<bool>("Auth:DemoUsers");
        const string selector = "EntraOrDemo";

        var auth = services.AddAuthentication(selector)
            .AddPolicyScheme(selector, selector, o => o.ForwardDefaultSelector = ctx =>
                ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || !demoEnabled
                    ? JwtBearerDefaults.AuthenticationScheme
                    : DemoAuthHandler.SchemeName)
            .AddJwtBearer(o =>
            {
                // Entra ID: https://login.microsoftonline.com/{tenant}/v2.0 and the API's client id.
                o.Authority = config["Auth:Entra:Authority"];
                o.Audience = config["Auth:Entra:Audience"];
                o.MapInboundClaims = false;
                o.TokenValidationParameters.RoleClaimType = "roles";
            });

        if (demoEnabled)
            auth.AddScheme<AuthenticationSchemeOptions, DemoAuthHandler>(DemoAuthHandler.SchemeName, null);

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(ReviewerPolicy, p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Reviewer));

        return services;
    }
}
