using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Introspects GraphQL schemas via HTTP and caches results per endpoint.
/// Also provides operation-name extraction from GraphQL documents.
/// </summary>
public sealed class GraphQlSchemaService(
    IHttpClientFactory httpClientFactory,
    IVariableSubstitutionService substitution) : IGraphQlSchemaService
{
    // Introspection query — returns type names and field names for completion
    private const string IntrospectionQuery = """
        {
          __schema {
            types {
              name
              kind
              fields {
                name
                type {
                  name
                  kind
                  ofType {
                    name
                    kind
                  }
                }
              }
            }
            queryType { name }
            mutationType { name }
            subscriptionType { name }
          }
        }
        """;

    private readonly ConcurrentDictionary<string, GraphQlIntrospectionResult> _cache = new(StringComparer.OrdinalIgnoreCase);

    // Regex matching named operation declarations:  query Foo  mutation Bar  subscription Baz
    private static readonly Regex OperationNameRegex = new(
        @"\b(?:query|mutation|subscription)\s+([A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<GraphQlIntrospectionResult> IntrospectAsync(
        string endpointUrl,
        ApiCollection collection,
        ApiEnvironment? activeEnvironment,
        CancellationToken cancellationToken = default)
    {
        var scope = await substitution.BuildScopeAsync(collection.Variables, activeEnvironment, cancellationToken);
        var resolvedUrl = substitution.Substitute(endpointUrl, scope);

        try
        {
            using var client = httpClientFactory.CreateClient("ApiClient");
            var body = JsonSerializer.Serialize(new { query = IntrospectionQuery });
            using var content = new StringContent(body, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

            using var response = await client.PostAsync(resolvedUrl, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            var result = new GraphQlIntrospectionResult
            {
                EndpointUrl = resolvedUrl,
                SchemaJson = json,
            };

            _cache[resolvedUrl] = result;
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var result = new GraphQlIntrospectionResult
            {
                EndpointUrl = resolvedUrl,
                ErrorMessage = $"Introspection failed: {ex.Message}",
            };
            return result;
        }
    }

    public IReadOnlyList<string> ParseOperationNames(string queryDocument)
    {
        if (string.IsNullOrWhiteSpace(queryDocument))
            return [];

        var names = new List<string>();
        foreach (Match match in OperationNameRegex.Matches(queryDocument))
        {
            if (match.Groups[1].Success)
                names.Add(match.Groups[1].Value);
        }

        return names;
    }

    public GraphQlIntrospectionResult? GetCachedSchema(string endpointUrl) =>
        _cache.TryGetValue(endpointUrl, out var result) ? result : null;

    public void ClearCache(string endpointUrl) => _cache.TryRemove(endpointUrl, out _);
}
