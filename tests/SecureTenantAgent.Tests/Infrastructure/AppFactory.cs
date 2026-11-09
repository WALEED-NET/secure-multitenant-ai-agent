using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SecureTenantAgent.Api.Data;

namespace SecureTenantAgent.Tests.Infrastructure;

/// <summary>Runs the real API in memory: real auth, real EF Core filters, real agent. Only the model is scripted.</summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _db = new("Data Source=:memory:");
    public ScriptedChatClient Model { get; } = new();

    public AppFactory() => _db.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Auth:DemoUsers", "true");
        builder.UseSetting("RateLimit:PerMinute", "1000");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_db));

            services.RemoveAll<IChatClient>();
            services.AddSingleton<IChatClient>(Model);
        });
    }

    public HttpClient As(string demoUser)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Demo-User", demoUser);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _db.Dispose();
    }
}

public static class HttpExtensions
{
    public static Task<HttpResponseMessage> AskAsync(this HttpClient client, string question) =>
        client.PostAsJsonAsync("/api/assistant/ask", new { question });
}
