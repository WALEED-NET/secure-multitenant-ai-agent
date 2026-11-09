using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SecureTenantAgent.Api.Agent;
using SecureTenantAgent.Api.Auth;
using SecureTenantAgent.Api.Data;
using SecureTenantAgent.Api.Endpoints;
using SecureTenantAgent.Api.Tenancy;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddAppAuthentication(config);

builder.Services.AddDbContext<AppDbContext>(o =>
{
    var sqlServer = config.GetConnectionString("SqlServer");
    if (!string.IsNullOrWhiteSpace(sqlServer)) o.UseSqlServer(sqlServer);
    else o.UseSqlite(config.GetConnectionString("Sqlite") ?? "Data Source=demo.db");
});

builder.Services.AddChatModel(config);
builder.Services.AddScoped<AgentRunContext>();
builder.Services.AddScoped<TenantTools>();
builder.Services.AddScoped<AssistantService>();

// Model calls cost money: limit each user, not just each IP.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("per-user", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.FindFirst("oid")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("RateLimit:PerMinute", 20), Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("SecureTenantAgent"))
    .WithTracing(t => t
        .AddSource(AssistantService.Telemetry.Name, "SecureTenantAgent.Model", "Microsoft.Agents.AI*")
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter());

builder.Services.AddProblemDetails();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    await SeedData.SeedAsync(scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>());

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/", () => "Secure multi-tenant AI agent demo. See README.").AllowAnonymous();
app.MapAssistantEndpoints();

app.Run();

public partial class Program;
