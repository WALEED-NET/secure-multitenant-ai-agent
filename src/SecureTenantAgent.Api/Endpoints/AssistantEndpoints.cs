using Microsoft.EntityFrameworkCore;
using SecureTenantAgent.Api.Agent;
using SecureTenantAgent.Api.Auth;
using SecureTenantAgent.Api.Data;
using SecureTenantAgent.Api.Domain;
using SecureTenantAgent.Api.Tenancy;

namespace SecureTenantAgent.Api.Endpoints;

public static class AssistantEndpoints
{
    public static void MapAssistantEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireRateLimiting("per-user");

        api.MapPost("/assistant/ask", async (AskRequest request, AssistantService assistant, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 2000)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["question"] = ["Ask a question of up to 2000 characters."] });

            AssistantService.Result result;
            try
            {
                result = await assistant.AskAsync(request.Question, ct);
            }
            catch (ModelNotConfiguredException ex)
            {
                return Results.Problem(title: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return result.Response is not null
                ? Results.Ok(result.Response)
                : Results.Problem(title: "The answer failed validation and was not returned.",
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    extensions: new Dictionary<string, object?> { ["problems"] = result.Problems });
        });

        api.MapGet("/actions/pending", async (AppDbContext db, CancellationToken ct) =>
            await db.PendingActions.AsNoTracking().Where(a => a.Status == ReviewStatus.Pending)
                .OrderBy(a => a.RequestedAtUtc).ToListAsync(ct))
            .RequireAuthorization(AuthSetup.ReviewerPolicy);

        api.MapPost("/actions/{id:guid}/{decision:regex(^(approve|reject)$)}",
            async (Guid id, string decision, AppDbContext db, ITenantContext tenant, CancellationToken ct) =>
            {
                var action = await db.PendingActions.FirstOrDefaultAsync(a => a.Id == id, ct);
                if (action is null) return Results.NotFound();
                if (action.Status != ReviewStatus.Pending) return Results.Conflict("Already reviewed.");

                action.Status = decision == "approve" ? ReviewStatus.Approved : ReviewStatus.Rejected;
                action.ReviewedBy = tenant.UserId;
                await db.SaveChangesAsync(ct);
                return Results.Ok(action);
            })
            .RequireAuthorization(AuthSetup.ReviewerPolicy);

        api.MapGet("/audit", async (AppDbContext db, CancellationToken ct) =>
            await db.AuditEntries.AsNoTracking().OrderByDescending(a => a.Id).Take(100).ToListAsync(ct))
            .RequireAuthorization(AuthSetup.ReviewerPolicy);
    }
}
