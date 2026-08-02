using Microsoft.Extensions.DependencyInjection;
using SwebKit.Agents;
using SwebKit.Agents.Tools;
using SwebKit.Agents.Tools.ApiClient;
using SwebKit.Agents.Tools.Redis;
using SwebKit.Agents.Tools.Storage;
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
    /// Registers AI agent infrastructure: model client, agent context builder,
    /// all agent tools, tool registry, and chat service.
    /// </summary>
    public static IServiceCollection AddSwebKitAgents(this IServiceCollection services)
    {
        services.AddHttpClient<IAgentModelClient, OpenAiCompatibleAgentClient>();
        services.AddHttpClient<AgentCapabilityTester>();

        services.AddSingleton<IAgentContextBuilder, AgentContextBuilder>();

        // Action coordinator for proposal/confirmation flow
        services.AddSingleton<IAgentActionCoordinator, AgentActionCoordinator>();

        // API Client agent service
        services.AddSingleton<IApiClientAgentService, ApiClientAgentService>();

        // Action executors — one per feature area, dispatched by AgentActionApplier via
        // IAgentActionExecutor.CanHandle, so a new area's executor can be added without
        // AgentActionApplier itself changing.
        services.AddSingleton<IAgentActionExecutor, ApiClientActionExecutor>();
        services.AddSingleton<IAgentActionExecutor, RedisActionExecutor>();
        services.AddSingleton<IAgentActionExecutor, StorageActionExecutor>();

        // Action applier for confirmed action execution
        services.AddSingleton<AgentActionApplier>();

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

        // Redis Tools
        services.AddSingleton<IAgentTool, GetRedisKeyInfoTool>();
        services.AddSingleton<IAgentTool, ListRedisKeysTool>();
        services.AddSingleton<IAgentTool, AnalyzeCacheHealthTool>();
        services.AddSingleton<IAgentTool, ProposeDeleteRedisKeyTool>();
        services.AddSingleton<IAgentTool, ProposeSetRedisKeyTtlTool>();

        // Storage Tools
        services.AddSingleton<IAgentTool, ListStorageBlobsTool>();
        services.AddSingleton<IAgentTool, GetStorageBlobPropertiesTool>();
        services.AddSingleton<IAgentTool, ProposeCopyBlobTool>();

        // Observability Tools
        services.AddSingleton<IAgentTool, QueryLogsTool>();
        services.AddSingleton<IAgentTool, GetMetricsTool>();

        // API Client Tools
        services.AddSingleton<IAgentTool, SearchApiRequestsTool>();
        services.AddSingleton<IAgentTool, GetApiRequestTool>();
        services.AddSingleton<IAgentTool, ProposeApiRequestChangeTool>();
        services.AddSingleton<IAgentTool, ProposeApiRequestDeleteTool>();
        services.AddSingleton<IAgentTool, PrepareApiRequestExecutionTool>();

        services.AddSingleton<IAgentToolRegistry, AgentToolRegistry>();
        services.AddSingleton<IAgentChatService, AgentChatService>();

        return services;
    }
}
