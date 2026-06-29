using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.Agents;
using SwebKit.Agents.Tools;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;
using SwebKit.Kubernetes.AksClient;

// Phase 0 POC Console Application
// Simple console app to test Mistral AI integration with SwebKit

var serviceProvider = BuildServiceProvider();

try
{
    Console.WriteLine("=== SwebKit AI Agent - Phase 0 POC ===");
    Console.WriteLine("Testing Mistral AI integration with Kubernetes pod analysis");
    Console.WriteLine("Type 'exit' or 'quit' to end the session.");
    Console.WriteLine();

    var mistralClient = serviceProvider.GetRequiredService<IMistralClient>();
    var tool = serviceProvider.GetRequiredService<GetPodStatusTool>();

    var systemPrompt = "You are a Kubernetes expert assistant for SwebKit. Your role is to help users analyze and understand the health and status of their Kubernetes pods. You have access to tools that can retrieve real-time pod information. When asked about a specific pod, use the available tools to get the current status. Always provide clear, actionable insights and explanations.";

    var toolParameters = BuildToolParametersSchema();
    var tools = new List<ToolDefinition>
    {
        new ToolDefinition
        {
            Name = tool.Name,
            Description = tool.Description,
            ParametersSchema = toolParameters
        }
    };

    Console.WriteLine("System ready. Available tools: " + string.Join(", ", tools.Select(t => t.Name)));
    Console.WriteLine();

    while (true)
    {
        Console.Write("> ");
        var userInput = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(userInput))
            continue;

        if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase) || 
            userInput.Equals("quit", StringComparison.OrdinalIgnoreCase))
            break;

        try
        {
            Console.WriteLine("Sending request to Mistral AI...");
            var startTime = DateTime.UtcNow;
            
            var response = await mistralClient.ChatAsync(
                systemPrompt,
                userInput,
                tools,
                CancellationToken.None);

            var endTime = DateTime.UtcNow;
            var latency = endTime - startTime;

            Console.WriteLine();
            Console.WriteLine("=== AI Response ===");
            Console.WriteLine(response);
            Console.WriteLine();
            Console.WriteLine("=== Performance ===");
            Console.WriteLine("Latency: " + latency.TotalSeconds.ToString("F2") + " seconds");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
            if (!string.IsNullOrEmpty(ex.InnerException?.Message))
            {
                Console.WriteLine("Details: " + ex.InnerException.Message);
            }
            Console.WriteLine();
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine("Fatal error: " + ex.Message);
    Environment.Exit(1);
}

static JsonElement BuildToolParametersSchema()
{
    var parameters = new
    {
        type = "object",
        properties = new
        {
            pod_name = new
            {
                type = "string",
                description = "The name of the Kubernetes pod to analyze"
            },
            namespace_param = new
            {
                type = "string",
                description = "The Kubernetes namespace where the pod is located"
            }
        },
        required = new[] { "pod_name" }
    };

    return JsonSerializer.SerializeToDocument(parameters).RootElement;
}

static IServiceProvider BuildServiceProvider()
{
    var services = new ServiceCollection();

    // Configuration
    services.AddSingleton<MistralConfig>(new MistralConfig());
    
    // HTTP Client
    services.AddHttpClient();
    
    // Credential Store - for POC, use a simple implementation
    services.AddSingleton<ICredentialStore>(new SimpleConsoleCredentialStore());
    
    // SwebKit Core Services
    services.AddSingleton<AppStateService>();
    
    // AKS Client Factory
    services.AddSingleton<IAksClientFactory, AksClientFactory>();
    
    // Agent Services
    services.AddSingleton<IMistralClient, MistralHttpClient>();
    services.AddSingleton<GetPodStatusTool>();

    return services.BuildServiceProvider();
}

// Simple credential store for POC that reads from environment variables
public class SimpleConsoleCredentialStore : ICredentialStore
{
    public void Save(string key, string secret)
    {
        // For POC, just store in memory
        Environment.SetEnvironmentVariable(key, secret);
    }

    public string? Get(string key)
    {
        // Check environment variable first
        var value = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrEmpty(value))
            return value;
        
        // For POC: If API key not found, prompt user
        if (key == "SwebKit-Agent:Mistral-ApiKey" || key.EndsWith("Mistral-ApiKey"))
        {
            Console.Write("Mistral API key not found. Please enter your Mistral API key: ");
            var apiKey = Console.ReadLine();
            if (!string.IsNullOrEmpty(apiKey))
            {
                Save(key, apiKey);
                return apiKey;
            }
        }
        
        return null;
    }

    public void Delete(string key)
    {
        Environment.SetEnvironmentVariable(key, null);
    }

    public IReadOnlyList<string> ListKeys(string prefix = "")
    {
        return Array.Empty<string>();
    }
}