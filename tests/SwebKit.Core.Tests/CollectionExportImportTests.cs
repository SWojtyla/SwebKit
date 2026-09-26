using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

/// <summary>
/// Tests for <see cref="PostmanCollectionImporter"/>.
/// </summary>
public sealed class PostmanExportImportTests
{
    private static ApiCollection MakeCollection() => new()
    {
        Id = "c1",
        Name = "Postman Test",
        Variables = [new CollectionVariable { Key = "host", Value = "localhost", IsEnabled = true }],
        Nodes =
        [
            new ApiCollectionNode
            {
                Id = "n1",
                Type = ApiCollectionNodeType.Request,
                Name = "Ping",
                Request = new HttpRequestEntry
                {
                    Id = "r1",
                    Name = "Ping",
                    Method = ApiRequestMethod.Get,
                    Url = "http://{{host}}/ping",
                },
            },
        ],
    };

    [Fact]
    public async Task Import_ParsesNameAndRequests()
    {
        var importer = new PostmanCollectionImporter();
        // Minimal Postman v2.1 JSON
        const string postman = """
            {
              "info": {
                "name": "My Postman Collection",
                "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
              },
              "item": [
                {
                  "name": "Get Items",
                  "request": {
                    "method": "GET",
                    "url": { "raw": "https://api.example.com/items" }
                  }
                },
                {
                  "name": "Create Item",
                  "request": {
                    "method": "POST",
                    "url": { "raw": "https://api.example.com/items" },
                    "body": {
                      "mode": "raw",
                      "raw": "{ \"name\": \"test\" }",
                      "options": { "raw": { "language": "json" } }
                    }
                  }
                }
              ],
              "variable": [
                { "key": "baseUrl", "value": "https://api.example.com" }
              ]
            }
            """;

        var bytes = System.Text.Encoding.UTF8.GetBytes(postman);
        var result = await importer.ImportAsync(bytes);

        Assert.Empty(result.Warnings);
        Assert.Single(result.Collections);

        var collection = result.Collections[0];
        Assert.Equal("My Postman Collection", collection.Name);
        Assert.Equal(2, collection.Nodes.Count);
        Assert.Equal(2, result.RequestCount);

        // Variable should have been extracted as environment
        Assert.Single(result.Environments);
        Assert.Equal("My Postman Collection (imported)", result.Environments[0].Name);
        Assert.Equal(1, result.VariablesExtractedAsEnvironment);
    }

    [Fact]
    public async Task Import_NestedFolder_ParsesCorrectly()
    {
        const string postman = """
            {
              "info": {
                "name": "Nested",
                "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
              },
              "item": [
                {
                  "name": "Auth",
                  "item": [
                    {
                      "name": "Login",
                      "request": { "method": "POST", "url": { "raw": "https://api.test/login" } }
                    }
                  ]
                }
              ]
            }
            """;
        var bytes = System.Text.Encoding.UTF8.GetBytes(postman);
        var importer = new PostmanCollectionImporter();
        var result = await importer.ImportAsync(bytes);

        Assert.Single(result.Collections);
        var folder = result.Collections[0].Nodes[0];
        Assert.Equal(ApiCollectionNodeType.Folder, folder.Type);
        Assert.Single(folder.Children);
        Assert.Equal(1, result.RequestCount);
    }

    [Fact]
    public void CanImport_WithPostmanPayload_ReturnsTrue()
    {
        const string postman = """
            {"info":{"name":"x","schema":"https://schema.getpostman.com/json/collection/v2.1.0/collection.json"},"item":[]}
            """;
        var importer = new PostmanCollectionImporter();
        Assert.True(importer.CanImport(System.Text.Encoding.UTF8.GetBytes(postman)));
    }

    [Fact]
    public void CanImport_WithNonPostman_ReturnsFalse()
    {
        var importer = new PostmanCollectionImporter();
        Assert.False(importer.CanImport("""{"schemaVersion":1}"""u8.ToArray()));
    }
}

/// <summary>
/// Tests for <see cref="CollectionImportService"/> — name collision resolution.
/// </summary>
public sealed class CollectionImportServiceTests
{
    [Theory]
    [InlineData("My API", new string[0], "My API")]
    [InlineData("My API", new[] { "My API" }, "My API (2)")]
    [InlineData("My API", new[] { "My API", "My API (2)" }, "My API (3)")]
    [InlineData("My API", new[] { "My API", "My API (2)", "My API (3)" }, "My API (4)")]
    public void ResolveNameCollision_ProducesExpectedName(string input, string[] existing, string expected)
    {
        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var result = CollectionImportService.ResolveNameCollision(input, existingSet);
        Assert.Equal(expected, result);
    }
}

/// <summary>
/// Tests for <see cref="SwebKitEnvironmentImporter"/>.
/// </summary>
public sealed class SwebKitEnvironmentImporterTests
{
    [Fact]
    public async Task Import_ValidEnvironmentsJson_ReturnsEnvironments()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "environments": [
                {
                  "id": "e1",
                  "name": "Production",
                  "variables": [
                    { "key": "host", "value": "api.prod.com", "isEnabled": true, "secretSource": "Plain" }
                  ]
                }
              ],
              "uiState": {}
            }
            """;

        var importer = new SwebKitEnvironmentImporter();
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        Assert.True(importer.CanImport(bytes));

        var result = await importer.ImportAsync(bytes);
        Assert.Empty(result.Warnings);
        Assert.Single(result.Environments);
        Assert.Equal("Production", result.Environments[0].Name);
        Assert.Single(result.Environments[0].Variables);
        Assert.Equal("host", result.Environments[0].Variables[0].Key);
    }

    [Fact]
    public void CanImport_NonEnvironmentPayload_ReturnsFalse()
    {
        var importer = new SwebKitEnvironmentImporter();
        Assert.False(importer.CanImport("""{"notAnEnv":true}"""u8.ToArray()));
    }
}
