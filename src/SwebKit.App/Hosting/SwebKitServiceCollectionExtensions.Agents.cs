using Microsoft.Extensions.DependencyInjection;
using SwebKit.Agents;
using SwebKit.Agents.Tools;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.App.Hosting;

/// <summary>
/// Extension methods for connection warmup and AI agent services.
/// </summary>
public static partial class SwebKitServiceCollectionExtensions
{
    /// <summary>
    /// Registers connection warmup caches and the warmup service.
    /// </summary>
    public static IServiceCollection AddSwebKitConnectionWarmup(this IServiceCollection services)
    {
        services.AddSingleton<IAksWarmupCache, AksWarmupCache>();
        services.AddSingleton<IRedisWarmupCache, RedisWarmupCache>();
        services.AddSingleton<IServiceBusWarmupCache, ServiceBusWarmupCache>();
        services.AddSingleton<IConnectionWarmupService, ConnectionWarmupService>();
        services.AddSingleton<RedisOpsInsightsAggregator>();
        return services;
    }

    /// <summary>
    /// Registers AI agent infrastructure: Mistral client, agent context builder,
    /// all agent tools, tool registry, and chat service.
    /// </summary>
    public static IServiceCollection AddSwebKitAgents(this IServiceCollection services)
    {
        services.AddSingleton<MistralConfig>(sp =>
        {
            var store = sp.GetRequiredService<ICredentialStore>();
            return new MistralConfig
            {
                ApiKey = store.Get("SwebKit-Agent:Mistral-ApiKey") ?? string.Empty
            };
        });
        services.AddSingleton<IMistralClient, MistralHttpClient>();

        services.AddSingleton<IAgentContextBuilder, AgentContextBuilder>();

        // Tools — registered as IAgentTool so AgentToolRegistry receives them all via IEnumerable<IAgentTool>
        // Kubernetes Tools
        services.AddSingleton<IAgentTool, GetPodStatusTool>();
        services.AddSingleton<IAgentTool, ListNamespacesTool>();
        services.AddSingleton<IAgentTool, ListPodsTool>();
        services.AddSingleton<IAgentTool, GetPodLogsTool>();
        services.AddSingleton<IAgentTool, GetPodEventsTool>();
        services.AddSingleton<IAgentTool, InvestigatePodIssueTool>();

        // Service Bus Tools
        services.AddSingleton<IAgentTool, GetQueueStatsTool>();
        services.AddSingleton<IAgentTool, GetQueueMessagesTool>();
        services.AddSingleton<IAgentTool, AnalyzeQueueHealthTool>();

        // Observability Tools
        services.AddSingleton<IAgentTool, QueryLogsTool>();
        services.AddSingleton<IAgentTool, GetMetricsTool>();

        services.AddSingleton<IAgentToolRegistry, AgentToolRegistry>();
        services.AddSingleton<IAgentChatService, AgentChatService>();

        return services;
    }
}
