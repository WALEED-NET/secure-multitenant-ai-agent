# Secure Multi-Tenant AI Agent (.NET 10 + Microsoft Agent Framework)

A small, complete reference for adding an AI agent to an existing multi-tenant ASP.NET Core SaaS **without trusting the model**.
The agent answers from the caller's own documents, cites its sources, and can only *propose* changes for a human to approve.
Tenant isolation, authorization and provenance are enforced in C#, so they hold even when the model is confused or manipulated.

**Stack:** .NET 10 · ASP.NET Core minimal APIs · Microsoft Agent Framework 1.27 · Azure OpenAI (Microsoft Foundry) · Entra ID (JWT) · EF Core (SQL Server / SQLite) · OpenTelemetry · xUnit

---

## How a question flows

```text
 Request + Entra ID token (user, tenant, roles)
        |
        v
 [1] Authentication ........ Entra ID JWT (demo users only when explicitly enabled)
 [2] Tenant context ........ tenant and role come from the token only, never from the prompt
 [3] Agent ................. Agent Framework, structured output (JSON schema)
        |   tools: SearchDocuments, GetDocument, RequestAction
        v
 [4] Tools run as the user . EF Core global query filter per tenant and role + write guard
                             every call audited; retrieved text wrapped as untrusted data
 [5] Provenance validator .. each citation must be a document retrieved for this user in this
                             run, and its quote must appear in it; otherwise HTTP 422
 [6] Human review .......... the agent can queue an allow-listed action; only a reviewer of the
                             same tenant can approve it
```

## Threat model

| Threat | Control | Proven by |
|--------|---------|-----------|
| Cross-tenant data access through the model | Tenant comes from the token; EF Core query filter on every tenant-owned entity | `Another_tenants_document_cannot_be_read_even_when_the_model_asks_for_it` |
| Cross-tenant writes | `SaveChanges` guard rejects any tenant mismatch | `Writes_into_another_tenant_are_blocked_at_the_data_layer` |
| Prompt injection from retrieved content | Retrieved text marked untrusted; tools enforce authorization regardless of instructions; allow-listed actions only | `Prompt_injection_in_a_document_cannot_cross_tenants_or_change_data` |
| Hallucinated or leaked citations | Provenance validator rejects citations not retrieved in this run, and fabricated quotes | `An_answer_citing_a_document_that_was_never_retrieved_is_rejected`, `An_answer_with_a_fabricated_quote_is_rejected` |
| Privilege escalation inside a tenant | Role-filtered documents; reviewer policy for approvals | `Restricted_documents_are_only_visible_to_reviewers`, `Only_a_reviewer_of_the_same_tenant_can_approve_an_action` |
| The model changing data on its own | No write tool exists; actions are queued as pending | same as above |
| Cost abuse | Per-user rate limiting; question length limit | configuration |
| Secrets in code | `DefaultAzureCredential` (managed identity in Azure, Azure CLI locally); no keys in config | configuration |

## Tests without a model

The tests run the real API in memory (real auth, real EF Core filters, real Agent Framework function calling).
Only the model is replaced by a scripted `IChatClient`, so each test can play a helpful, confused or **malicious** model on purpose.
They are deterministic and free: no API key or Azure subscription is needed.

```bash
dotnet test
```

```text
Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10
```

| Test | What the scripted model tries |
|------|-------------------------------|
| `Answers_with_a_valid_citation_from_the_callers_tenant` | Normal question, correct citation: accepted |
| `Another_tenants_document_cannot_be_read_even_when_the_model_asks_for_it` | Reads another tenant's document by id: "not found" |
| `An_answer_citing_a_document_that_was_never_retrieved_is_rejected` | Cites another tenant's secret anyway: HTTP 422 |
| `An_answer_with_a_fabricated_quote_is_rejected` | Invents a quote: HTTP 422 |
| `Prompt_injection_in_a_document_cannot_cross_tenants_or_change_data` | Fully obeys an injected document: nothing leaks, nothing changes, all audited |
| `Restricted_documents_are_only_visible_to_reviewers` (x2) | Member vs reviewer searching restricted content |
| `Only_a_reviewer_of_the_same_tenant_can_approve_an_action` | Member and other-tenant reviewer try to approve |
| `Anonymous_requests_are_rejected` | No token: HTTP 401 |
| `Writes_into_another_tenant_are_blocked_at_the_data_layer` | Saves a document into another tenant: exception |

## Run locally

```bash
cd src/SecureTenantAgent.Api
dotnet user-secrets set "AI:Endpoint" "https://<your-resource>.openai.azure.com/"
dotnet user-secrets set "AI:Deployment" "<your-deployment>"
az login        # no API key needed: DefaultAzureCredential uses your Azure CLI sign-in
dotnet run --launch-profile http
```

```bash
curl -X POST http://localhost:5036/api/assistant/ask \
  -H "X-Demo-User: alice" -H "Content-Type: application/json" \
  -d '{"question":"What is the refund policy?"}'
```

Demo users (local only): `alice` (clinic, member), `omar` (clinic, reviewer), `bob` (legal, member), `lina` (legal, reviewer).

## Production with Entra ID

Set `Auth:DemoUsers` to `false`, then configure `Auth:Entra:Authority`, `Auth:Entra:Audience` and `Tenancy:TenantClaimType`
(for example a custom claim or the `tid` claim mapped to your tenant id). Give the App Service a managed identity with the
*Cognitive Services OpenAI User* role on the Azure OpenAI resource, so no key is ever stored.

## Where to extend

- **Retrieval:** `SearchDocuments` uses simple keyword matching; swap in Azure AI Search behind the same tool.
- **Evaluation:** add cases to the security tests, or point them at a real deployment to evaluate a model change.
