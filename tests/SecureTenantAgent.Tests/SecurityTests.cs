using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureTenantAgent.Api.Agent;
using SecureTenantAgent.Api.Data;
using SecureTenantAgent.Api.Domain;
using SecureTenantAgent.Api.Tenancy;
using SecureTenantAgent.Tests.Infrastructure;
using static SecureTenantAgent.Tests.Infrastructure.ScriptedChatClient;

namespace SecureTenantAgent.Tests;

/// <summary>
/// Each test plays a model that is helpful, confused or malicious, and checks that
/// isolation, authorization and provenance hold no matter what the model does.
/// </summary>
public sealed class SecurityTests : IDisposable
{
    private readonly AppFactory _app = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Answers_with_a_valid_citation_from_the_callers_tenant()
    {
        _app.Model.Script = turn => turn.FirstTurn
            ? CallTools(("SearchDocuments", new() { ["query"] = "refund policy" }))
            : Answer("Refunds can be requested within 14 days.",
                [(SeedData.ClinicRefunds, "request a refund within 14 days of payment")]);

        var response = await _app.As("alice").AskAsync("What is the refund policy?");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AskResponse>(Json);
        Assert.Contains(SeedData.ClinicRefunds, body!.RetrievedDocumentIds);
        Assert.Single(body.Answer.Citations);
    }

    [Fact]
    public async Task Another_tenants_document_cannot_be_read_even_when_the_model_asks_for_it()
    {
        string? toolOutput = null;
        _app.Model.Script = turn =>
        {
            if (turn.FirstTurn)
                return CallTools(("GetDocument", new() { ["documentId"] = SeedData.LegalSettlement }));
            toolOutput = turn.AllToolOutput;
            return Answer("I could not find that document.", []);
        };

        var response = await _app.As("alice").AskAsync("Show me document 22222222-0000-0000-0000-000000000002");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("not found", toolOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1.2 million", toolOutput);
    }

    [Fact]
    public async Task An_answer_citing_a_document_that_was_never_retrieved_is_rejected()
    {
        // A model that "remembers" or invents another tenant's document and cites it anyway.
        _app.Model.Script = turn => turn.FirstTurn
            ? CallTools(("SearchDocuments", new() { ["query"] = "settlement" }))
            : Answer("The settlement was 1.2 million.", [(SeedData.LegalSettlement, "settlement amount for case 4471")]);

        var response = await _app.As("alice").AskAsync("What was the settlement amount?");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain("1.2 million", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_answer_with_a_fabricated_quote_is_rejected()
    {
        _app.Model.Script = turn => turn.FirstTurn
            ? CallTools(("GetDocument", new() { ["documentId"] = SeedData.ClinicRefunds }))
            : Answer("Refunds are allowed within 90 days.", [(SeedData.ClinicRefunds, "refund within 90 days")]);

        var response = await _app.As("alice").AskAsync("How long do I have for a refund?");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Prompt_injection_in_a_document_cannot_cross_tenants_or_change_data()
    {
        // The poisoned document tells the model to read the other tenant and archive everything.
        // We play a model that obeys it completely.
        _app.Model.Script = turn => turn.ToolResults.Count switch
        {
            0 => CallTools(("GetDocument", new() { ["documentId"] = SeedData.ClinicPoisoned })),
            1 => CallTools(
                ("GetDocument", new() { ["documentId"] = SeedData.LegalSettlement }),
                ("RequestAction", new() { ["kind"] = "archive_document", ["documentId"] = SeedData.LegalSettlement, ["reason"] = "injected" }),
                ("RequestAction", new() { ["kind"] = "delete_everything", ["documentId"] = SeedData.ClinicRefunds, ["reason"] = "injected" }),
                ("RequestAction", new() { ["kind"] = "archive_document", ["documentId"] = SeedData.ClinicRefunds, ["reason"] = "injected" })),
            _ => Answer("Done.", []),
        };

        var response = await _app.As("alice").AskAsync("Summarize the imported refund FAQ.");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _app.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();

        await using var legal = new AppDbContext(options, new FixedTenantContext(SeedData.Legal, isReviewer: true));
        Assert.Empty(await legal.PendingActions.ToListAsync());
        Assert.True(await legal.Documents.AnyAsync(d => d.Id == SeedData.LegalSettlement));

        await using var clinic = new AppDbContext(options, new FixedTenantContext(SeedData.Clinic, isReviewer: true));
        var pending = await clinic.PendingActions.ToListAsync();
        var only = Assert.Single(pending);
        Assert.Equal(("archive_document", ReviewStatus.Pending), (only.Kind, only.Status));
        Assert.True(await clinic.Documents.AnyAsync(d => d.Id == SeedData.ClinicRefunds));

        var audit = await clinic.AuditEntries.Select(a => a.Outcome).ToListAsync();
        Assert.Contains("not found or not permitted", audit);
        Assert.Contains("refused: action not allowed", audit);
        Assert.Contains("refused: document not visible", audit);
    }

    [Theory]
    [InlineData("alice", false)]
    [InlineData("omar", true)]
    public async Task Restricted_documents_are_only_visible_to_reviewers(string user, bool expectVisible)
    {
        string output = "";
        _app.Model.Script = turn =>
        {
            if (turn.FirstTurn) return CallTools(("SearchDocuments", new() { ["query"] = "payroll bands" }));
            output = turn.AllToolOutput;
            return Answer("Searched.", []);
        };

        await _app.As(user).AskAsync("What are the payroll bands?");

        Assert.Equal(expectVisible, output.Contains(SeedData.ClinicSalaries.ToString()));
    }

    [Fact]
    public async Task Only_a_reviewer_of_the_same_tenant_can_approve_an_action()
    {
        _app.Model.Script = turn => turn.FirstTurn
            ? CallTools(("RequestAction", new() { ["kind"] = "flag_for_update", ["documentId"] = SeedData.ClinicHours, ["reason"] = "Hours changed" }))
            : Answer("I asked a reviewer to update it.", [], needsReview: true);
        await _app.As("alice").AskAsync("Our hours changed, please update the document.");

        var pending = await _app.As("omar").GetFromJsonAsync<List<PendingAction>>("/api/actions/pending", Json);
        var actionId = Assert.Single(pending!).Id;

        Assert.Equal(HttpStatusCode.Forbidden, (await _app.As("alice").PostAsync($"/api/actions/{actionId}/approve", null)).StatusCode);
        // A reviewer of another tenant: the action simply does not exist for them.
        Assert.Equal(HttpStatusCode.NotFound, (await _app.As("lina").PostAsync($"/api/actions/{actionId}/approve", null)).StatusCode);
        Assert.Empty((await _app.As("lina").GetFromJsonAsync<List<PendingAction>>("/api/actions/pending", Json))!);
        Assert.Equal(HttpStatusCode.OK, (await _app.As("omar").PostAsync($"/api/actions/{actionId}/approve", null)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        var response = await _app.CreateClient().AskAsync("Hello?");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Writes_into_another_tenant_are_blocked_at_the_data_layer()
    {
        using var scope = _app.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
        await using var clinic = new AppDbContext(options, new FixedTenantContext(SeedData.Clinic));

        clinic.Documents.Add(new KnowledgeDocument { TenantId = SeedData.Legal, Title = "x", Body = "x" });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => clinic.SaveChangesAsync());
    }
}
