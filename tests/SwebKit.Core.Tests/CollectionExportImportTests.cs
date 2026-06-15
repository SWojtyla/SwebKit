using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

/// <summary>
/// Tests for <see cref="SwebKitCollectionExporter"/> and <see cref="SwebKitCollectionImporter"/>.
/// </summary>
public sealed class SwebKitExportImportTests
{
    private static ApiCollection MakeCollection(string name = "My API") => new()
    {
        Id = "c1",
        Name = name,
        Variables = [new CollectionVariable { Key = "base_url", Value = "https://api.test", IsEnabled = true }],
        Nodes =
        [
            new ApiCollectionNode
            {
                Id = "n1",
                Type = ApiCollectionNodeType.Request,
                Name = "Get User",
                Request = new HttpRequestEntry
                {
                    Id = "r1",
                    Name = "Get User",
                    Method = ApiRequestMethod.Get,
                    Url = "{{base_url}}/users/1",
                    Headers = [new KeyValuePair<string> { Key = "Accept", Value = "application/json" }],
                    CaptureRules = [new CaptureRule { Id = "cr1", TargetVariable = "userId", Source = CaptureSource.StatusCode, IsEnabled = true }],
                },
            },
            new ApiCollectionNode
            {
                Id = "n2",
                Type = ApiCollectionNodeType.Folder,
                Name = "Auth",
                Children =
                [
                    new ApiCollectionNode
                    {
                        Id = "n3",
                        Type = ApiCollectionNodeType.Request,
                        Name = "Login",
                        Request = new HttpRequestEntry
                        {
                            Id = "r2",
                            Name = "Login",
                            Method = ApiRequestMethod.Post,
                            Url = "{{base_url}}/auth/login",
                            Auth = new AuthConfig { Type = AuthType.BearerToken, CredentialKey = "login-token" },
                        },
                    },
                ],
            },
        ],
    };

    // ── Lossless round-trip ────────────────────────────────────────────────────

    [Fact]
    public async Task ExportThenImport_LosslessRoundTrip()
    {
        var exporter = new SwebKitCollectionExporter();
        var importer = new SwebKitCollectionImporter();
        var original = MakeCollection();
        var env = new ApiEnvironment { Id = "e1", Name = "Prod", Variables = [new EnvironmentVariable { Key = "token", Value = "secret" }] };

        var bytes = await exporter.ExportAsync(original, [env]);
        var result = await importer.ImportAsync(bytes);

        Assert.Empty(result.Warnings);
        Assert.Single(result.Collections);
        Assert.Single(result.Environments);

        var imported = result.Collections[0];
        Assert.Equal("My API", imported.Name);
        Assert.Equal(2, imported.Nodes.Count);
        Assert.Equal("Get User", imported.Nodes[0].Name);
        Assert.Equal(ApiCollectionNodeType.Folder, imported.Nodes[1].Type);
        Assert.Single(imported.Nodes[1].Children);
    }

    [Fact]
    public async Task Import_CountsRequests()
    {
        var exporter = new SwebKitCollectionExporter();
        var importer = new SwebKitCollectionImporter();
        var bytes = await exporter.ExportAsync(MakeCollection(), []);
        var result = await importer.ImportAsync(bytes);

        // 2 requests: root-level + inside folder
        Assert.Equal(2, result.RequestCount);
    }

    [Fact]
    public async Task Import_CountsCaptureRulesAndAuthConfigs()
    {
        var exporter = new SwebKitCollectionExporter();
        var importer = new SwebKitCollectionImporter();
        var bytes = await exporter.ExportAsync(MakeCollection(), []);
        var result = await importer.ImportAsync(bytes);

        Assert.Equal(1, result.CaptureRuleCount);
        Assert.Equal(1, result.AuthConfigsRequiringReEntry);
    }

    [Fact]
    public async Task CanImport_WithValidBytes_ReturnsTrue()
    {
        var exporter = new SwebKitCollectionExporter();
        var importer = new SwebKitCollectionImporter();
        var bytes = await exporter.ExportAsync(MakeCollection(), []);
        Assert.True(importer.CanImport(bytes));
    }

    [Fact]
    public void CanImport_WithGarbage_ReturnsFalse()
    {
        var importer = new SwebKitCollectionImporter();
        Assert.False(importer.CanImport("not json at all"u8.ToArray()));
    }
}

/// <summary>
/// Tests for <see cref="PostmanCollectionExporter"/> and <see cref="PostmanCollectionImporter"/>.
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
    public async Task Export_ProducesValidPostmanSchema()
    {
        var exporter = new PostmanCollectionExporter();
        var bytes = await exporter.ExportAsync(MakeCollection(), []);
        var json = System.Text.Encoding.UTF8.GetString(bytes);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var schema = doc.RootElement.GetProperty("info").GetProperty("schema").GetString() ?? "";
        Assert.Contains("getpostman.com", schema);
        Assert.Contains("collection", schema);
    }

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

/// <summary>
/// Tests for <see cref="BrunoCollectionExporter"/> — validates ZIP structure.
/// </summary>
public sealed class BrunoCollectionExporterTests
{
    [Fact]
    public async Task Export_ProducesZipWithBrunoJson()
    {
        var exporter = new BrunoCollectionExporter();
        var collection = new ApiCollection
        {
            Id = "c1",
            Name = "Bruno Test",
            Nodes =
            [
                new ApiCollectionNode
                {
                    Id = "n1",
                    Type = ApiCollectionNodeType.Request,
                    Name = "Hello World",
                    Request = new HttpRequestEntry
                    {
                        Id = "r1",
                        Name = "Hello World",
                        Method = ApiRequestMethod.Get,
                        Url = "https://api.example.com",
                    },
                },
            ],
        };

        var bytes = await exporter.ExportAsync(collection, []);

        using var ms = new MemoryStream(bytes);
        using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);

        var entries = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains(entries, e => e.EndsWith("bruno.json"));
        Assert.Contains(entries, e => e.EndsWith(".bru") && e.Contains("Hello World"));
    }

    [Fact]
    public async Task Export_FolderBecomesSubdirectory()
    {
        var exporter = new BrunoCollectionExporter();
        var collection = new ApiCollection
        {
            Id = "c1",
            Name = "Nested",
            Nodes =
            [
                new ApiCollectionNode
                {
                    Id = "f1",
                    Type = ApiCollectionNodeType.Folder,
                    Name = "Auth",
                    Children =
                    [
                        new ApiCollectionNode
                        {
                            Id = "n1",
                            Type = ApiCollectionNodeType.Request,
                            Name = "Login",
                            Request = new HttpRequestEntry { Id = "r1", Name = "Login", Url = "https://api.test" },
                        },
                    ],
                },
            ],
        };

        var bytes = await exporter.ExportAsync(collection, []);

        using var ms = new MemoryStream(bytes);
        using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains(entries, e => e.Contains("/Auth/") && e.EndsWith("Login.bru"));
    }
}
