using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwebKit.Agents;
using SwebKit.Agents.Tools;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Kubernetes.AksClient;

// ─────────────────────────────────────────────
//  SwebKit AI Agent — Phase 0 PoC Console
// ─────────────────────────────────────────────
var serviceProvider = BuildServiceProvider();
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    PrintBanner();

    var mistralClient = serviceProvider.GetRequiredService<IMistralClient>();
    var podStatusTool = serviceProvider.GetRequiredService<GetPodStatusTool>();
    var listNsTool = serviceProvider.GetRequiredService<ListNamespacesTool>();

    // ── Tool executor — dispatches Mistral tool calls to local implementations ──
    var toolExecutor = async (string toolName, JsonElement args, CancellationToken ct) =>
    {
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.Write($"  ⚙  {toolName}");
        if (args.ValueKind == JsonValueKind.Object)
        {
            var argPairs = args.EnumerateObject()
                              .Select(p => $"{p.Name}={p.Value}")
                              .ToArray();
            if (argPairs.Length > 0)
                Console.Write($"({string.Join(", ", argPairs)})");
        }
        Console.WriteLine(" ...");
        Console.ResetColor();

        var result = toolName switch
        {
            "get_pod_status" => await podStatusTool.ExecuteAsync(args, ct),
            "list_namespaces" => await listNsTool.ExecuteAsync(args, ct),
            _ => JsonSerializer.Serialize(new { error = $"Unknown tool: {toolName}" })
        };

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  ✓ {result.Length} bytes");
        Console.ResetColor();
        return result;
    };

    var systemPrompt =
        "You are a Kubernetes expert assistant embedded in SwebKit, a desktop operations tool. " +
        "You have access to tools that query live cluster data. " +
        "When the user asks about pods or namespaces, call the appropriate tool instead of guessing. " +
        "Always be concise and actionable.";

    var tools = BuildToolDefinitions(podStatusTool, listNsTool);

    Console.WriteLine($"Tools available: {string.Join(", ", tools.Select(t => t.Name))}");
    Console.WriteLine();

    while (!cts.Token.IsCancellationRequested)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("> ");
        Console.ResetColor();

        string? userInput;
        try
        {
            userInput = Console.ReadLine();
        }
        catch (OperationCanceledException)
        {
            break;
        }

        if (userInput is null || cts.Token.IsCancellationRequested)
            break;

        if (string.IsNullOrWhiteSpace(userInput))
            continue;

        if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
            userInput.Equals("quit", StringComparison.OrdinalIgnoreCase))
            break;

        if (userInput.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            PrintHelp();
            continue;
        }

        try
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  Thinking...");
            Console.ResetColor();

            var sw = System.Diagnostics.Stopwatch.StartNew();

            var response = await mistralClient.ChatAsync(
                systemPrompt,
                userInput,
                tools,
                toolExecutor,
                cts.Token);

            sw.Stop();

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("─── Response " + new string('─', 60));
            Console.ResetColor();
            Console.WriteLine(response);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"─── {sw.Elapsed.TotalSeconds:F1}s " + new string('─', 60));
            Console.ResetColor();
            Console.WriteLine();
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            Console.WriteLine("Cancelled.");
            break;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  Error: {ex.Message}");
            if (ex.InnerException is not null)
                Console.WriteLine($"  Detail: {ex.InnerException.Message}");
            Console.ResetColor();
            Console.WriteLine();
        }
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Fatal: {ex.Message}");
    Console.ResetColor();
    return 1;
}

Console.WriteLine("Bye.");
return 0;

// ─── Helpers ─────────────────────────────────────────────────────────────────

static void PrintBanner()
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine(@"
 ╔══════════════════════════════════════════╗
 ║   SwebKit AI Agent  ·  Phase 0 PoC      ║
 ║   Mistral  +  Kubernetes  tool calling  ║
 ╚══════════════════════════════════════════╝");
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine("Set MISTRAL_API_KEY or SWEBOOK-Agent:Mistral-ApiKey env var before starting.");
    Console.WriteLine("Type  help  for example queries, or  exit  to quit.");
    Console.WriteLine();
}

static void PrintHelp()
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine(@"
Example queries:
  list my namespaces
  list all namespaces
  what pods are in the default namespace?
  check the status of pod <name>
  is pod <name> in namespace kube-system healthy?
  why does pod <name> keep restarting?
  what's wrong with pod <name>?

Commands:
  help   — show this message
  exit   — quit
");
    Console.ResetColor();
}

static List<ToolDefinition> BuildToolDefinitions(
    GetPodStatusTool podStatusTool,
    ListNamespacesTool listNsTool)
{
    var podStatusSchema = JsonSerializer.SerializeToDocument(new
    {
        type = "object",
        properties = new
        {
            pod_name = new
            {
                type = "string",
                description = "The name of the Kubernetes pod to inspect."
            },
            @namespace = new
            {
                type = "string",
                description = "The Kubernetes namespace. Defaults to 'default' if omitted."
            }
        },
        required = new[] { "pod_name" }
    }).RootElement;

    var listNsSchema = JsonSerializer.SerializeToDocument(new
    {
        type = "object",
        properties = new { },
        required = Array.Empty<string>()
    }).RootElement;

    return
    [
        new ToolDefinition
        {
            Name             = podStatusTool.Name,
            Description      = podStatusTool.Description,
            ParametersSchema = podStatusSchema
        },
        new ToolDefinition
        {
            Name             = listNsTool.Name,
            Description      = listNsTool.Description,
            ParametersSchema = listNsSchema
        }
    ];
}

static IServiceProvider BuildServiceProvider()
{
    var services = new ServiceCollection();

    services.AddSingleton<MistralConfig>(new MistralConfig());
    services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
    services.AddHttpClient();
    services.AddSingleton<ICredentialStore>(new SimpleConsoleCredentialStore());
    services.AddSingleton<IAppEventBus, AppEventBus>();
    services.AddSingleton<AppStateService>(sp => new AppStateService(
        new ProfileRepository(),
        new UiStateRepository(),
        sp.GetRequiredService<IAppEventBus>()));
    services.AddSingleton<IAksClientFactory, AksClientFactory>();
    services.AddSingleton<IMistralClient, MistralHttpClient>();
    services.AddSingleton<GetPodStatusTool>();
    services.AddSingleton<ListNamespacesTool>();

    return services.BuildServiceProvider();
}

// ─── Credential Store ─────────────────────────────────────────────────────────

public sealed class SimpleConsoleCredentialStore : ICredentialStore
{
    private const string EnvVar = "MISTRAL_API_KEY";
    private const string StoreKey = "SwebKit-Agent:Mistral-ApiKey";

    public string? Get(string key)
    {
        // Canonical env var first
        var value = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrEmpty(value)) return value;

        // Fallback: exact env var name used as key
        value = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrEmpty(value)) return value;

        // Interactive fallback for API key only
        if (key == StoreKey || key.Contains("Mistral-ApiKey"))
        {
            Console.Write("Enter Mistral API key: ");
            var entered = Console.ReadLine();
            if (!string.IsNullOrEmpty(entered))
            {
                Environment.SetEnvironmentVariable(EnvVar, entered);
                return entered;
            }
        }

        return null;
    }

    public void Save(string key, string secret) => Environment.SetEnvironmentVariable(key, secret);
    public void Delete(string key) => Environment.SetEnvironmentVariable(key, null);
    public IReadOnlyList<string> ListKeys(string prefix = "") => [];
}