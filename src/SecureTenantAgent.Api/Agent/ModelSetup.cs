using System.ClientModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;

namespace SecureTenantAgent.Api.Agent;

public static class ModelSetup
{
    /// <summary>
    /// Azure OpenAI (Microsoft Foundry) deployment. Without an API key it signs in with
    /// <see cref="DefaultAzureCredential"/>: a managed identity in Azure, your Azure CLI login locally.
    /// No secret ever lives in code or appsettings.
    /// </summary>
    public static IServiceCollection AddChatModel(this IServiceCollection services, IConfiguration config)
    {
        var endpoint = config["AI:Endpoint"];
        var deployment = config["AI:Deployment"];

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(deployment))
        {
            services.AddSingleton<IChatClient, NotConfiguredChatClient>();
            return services;
        }

        services.AddSingleton<IChatClient>(sp =>
        {
            var apiKey = config["AI:ApiKey"];
            var client = string.IsNullOrWhiteSpace(apiKey)
                ? new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
                : new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey));

            return client.GetChatClient(deployment).AsIChatClient()
                .AsBuilder()
                .UseOpenTelemetry(sourceName: "SecureTenantAgent.Model", configure: o => o.EnableSensitiveData = false)
                .Build(sp);
        });
        return services;
    }

    private sealed class NotConfiguredChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default) =>
            throw new ModelNotConfiguredException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default) =>
            throw new ModelNotConfiguredException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}

public sealed class ModelNotConfiguredException()
    : InvalidOperationException("No model is configured. Set AI:Endpoint and AI:Deployment (see README).");
