using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class LinkedCollectionRootTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "swebkit-linked-tests", Guid.NewGuid().ToString("N"));
    private readonly LinkedCollectionFileService _fileService = new(new LinkedGitService());

    [Fact]
    public async Task EnsureRootAsync_CreatesManifestAndFolders()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");

        Assert.Equal(Path.Combine(_root, ".swebkit-api"), apiRoot);
        Assert.True(File.Exists(Path.Combine(apiRoot, "swebkit.json")));
        Assert.True(Directory.Exists(Path.Combine(apiRoot, "collections")));
        Assert.True(Directory.Exists(Path.Combine(apiRoot, "environments")));
    }

    [Fact]
    public async Task LoadRootAsync_SparseRequest_InfersDefaults()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collectionPath = Path.Combine(apiRoot, "collections", "orders");
        Directory.CreateDirectory(collectionPath);
        await File.WriteAllTextAsync(Path.Combine(collectionPath, "get-order.swebreq.json"), """
            {
              "method": "Get",
              "url": "{{baseUrl}}/orders/{{orderId}}"
            }
            """);

        var result = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });

        var collection = Assert.Single(result.Collections);
        Assert.Equal("Orders", collection.Name);
        var node = Assert.Single(collection.Nodes);
        Assert.NotNull(node.Request);
        Assert.Equal("Get Order", node.Request.Name);
        Assert.Equal(ApiRequestMethod.Get, node.Request.Method);
        Assert.Equal("{{baseUrl}}/orders/{{orderId}}", node.Request.Url);
        Assert.Empty(node.Request.Headers);
        Assert.Empty(node.Request.QueryParams);
        Assert.Null(node.Request.Auth);
    }

    [Fact]
    public async Task LoadRootAsync_GraphQlRequest_LoadsSidecars()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collectionPath = Path.Combine(apiRoot, "collections", "admin");
        Directory.CreateDirectory(collectionPath);
        await File.WriteAllTextAsync(Path.Combine(collectionPath, "users.swebreq.json"), """
            {
              "method": "GraphQl",
              "url": "{{graphqlUrl}}",
              "queryFile": "users.graphql",
              "variablesFile": "users.variables.json"
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(collectionPath, "users.graphql"), "query Users { users { id name } }");
        await File.WriteAllTextAsync(Path.Combine(collectionPath, "users.variables.json"), "{ \"take\": 20 }");

        var result = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });

        var request = Assert.Single(Assert.Single(result.Collections).Nodes).Request;
        Assert.NotNull(request);
        Assert.Equal(ApiRequestMethod.GraphQl, request.Method);
        Assert.Equal("query Users { users { id name } }", request.GraphQlQuery);
        Assert.Equal("{ \"take\": 20 }", request.GraphQlVariables);
    }

    [Fact]
    public async Task SaveRequestAsync_WritesJsonAndGraphQlSidecars()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collectionPath = Path.Combine(apiRoot, "collections", "admin");
        Directory.CreateDirectory(collectionPath);
        await File.WriteAllTextAsync(Path.Combine(collectionPath, "users.swebreq.json"), """
            {
              "method": "GraphQl",
              "url": "{{graphqlUrl}}",
              "queryFile": "users.graphql"
            }
            """);

        var result = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });
        var collection = Assert.Single(result.Collections);
        var request = Assert.Single(collection.Nodes).Request!;
        request.GraphQlQuery = "query Users { users { id } }";
        request.GraphQlVariables = "{\n  \"take\": 10\n}";
        request.Body.Mode = RequestBodyMode.Json;
        request.Body.RawContent = "{\n  \"sample\": true\n}";

        await _fileService.SaveRequestAsync(result.ApiRootPath, collection, request);

        Assert.True(File.Exists(Path.Combine(collectionPath, "users.graphql")));
        Assert.True(File.Exists(Path.Combine(collectionPath, "users.variables.json")));
        Assert.True(File.Exists(Path.Combine(collectionPath, "users.body.json")));
        Assert.Contains("query Users", await File.ReadAllTextAsync(Path.Combine(collectionPath, "users.graphql")));
        Assert.Contains("sample", await File.ReadAllTextAsync(Path.Combine(collectionPath, "users.body.json")));
    }

    [Fact]
    public async Task SaveRequestAsync_WritesResponseExamples()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collectionPath = Path.Combine(apiRoot, "collections", "orders");
        Directory.CreateDirectory(collectionPath);
        await File.WriteAllTextAsync(Path.Combine(collectionPath, "get-order.swebreq.json"), """
            {
              "method": "Get",
              "url": "/orders/1"
            }
            """);

        var result = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });
        var collection = Assert.Single(result.Collections);
        var request = Assert.Single(collection.Nodes).Request!;
        request.ResponseExamples.Add(new ResponseExample
        {
            Id = "example-1",
            Name = "200 example",
            StatusCode = 200,
            StatusText = "200 OK",
            Body = "{\"ok\":true}",
        });

        await _fileService.SaveRequestAsync(result.ApiRootPath, collection, request);
        var json = await File.ReadAllTextAsync(Path.Combine(collectionPath, "get-order.swebreq.json"));

        Assert.Contains("responseExamples", json);
        Assert.Contains("200 example", json);
    }

    [Fact]
    public async Task SaveRequestAsync_WhenFileChangedExternally_ReturnsConflict()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collectionPath = Path.Combine(apiRoot, "collections", "orders");
        Directory.CreateDirectory(collectionPath);
        var requestPath = Path.Combine(collectionPath, "get-order.swebreq.json");
        await File.WriteAllTextAsync(requestPath, """
            {
              "method": "Get",
              "url": "{{baseUrl}}/orders/1"
            }
            """);

        var result = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });
        var collection = Assert.Single(result.Collections);
        var request = Assert.Single(collection.Nodes).Request!;
        var stamp = Assert.Single(result.RequestFiles).ContentStamp;

        await File.WriteAllTextAsync(requestPath, """
            {
              "method": "Get",
              "url": "{{baseUrl}}/orders/2"
            }
            """);
        request.Url = "{{baseUrl}}/orders/3";

        var saveResult = await _fileService.SaveRequestAsync(result.ApiRootPath, collection, request, stamp);

        Assert.False(saveResult.IsSuccess);
        Assert.True(saveResult.HasConflict);
        Assert.Contains("orders/2", await File.ReadAllTextAsync(requestPath));
    }

    [Fact]
    public async Task LoadRootAsync_EnvironmentFile_LoadsPlainVariablesAndSecretReferences()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var envPath = Path.Combine(apiRoot, "environments", "dev.swebenv.json");
        await File.WriteAllTextAsync(envPath, """
                        {
                            "name": "dev",
                            "variables": {
                                "baseUrl": "https://dev.example.com"
                            },
                            "secrets": {
                                "apiToken": {
                                    "provider": "CredentialStore",
                                    "ref": "project/dev/api-token"
                                }
                            }
                        }
                        """);

        var result = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });

        var environment = Assert.Single(result.Environments);
        Assert.Equal("dev", environment.Name);
        Assert.Contains(environment.Variables, variable => variable.Key == "baseUrl" && variable.Value == "https://dev.example.com");
        Assert.Contains(environment.Variables, variable => variable.Key == "secret:apiToken" && variable.CredentialKey == "project/dev/api-token");
        Assert.Equal(environment.Id, Assert.Single(result.EnvironmentFiles).EnvironmentId);
    }

    [Fact]
    public async Task SaveEnvironmentAsync_WritesSecretReferencesWithoutSecretValues()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var envPath = Path.Combine(apiRoot, "environments", "dev.swebenv.json");
        var environment = new ApiEnvironment
        {
            Id = "dev",
            Name = "dev",
            Variables =
                [
                        new EnvironmentVariable { Key = "baseUrl", Value = "https://dev.example.com", IsEnabled = true },
                                new EnvironmentVariable
                                {
                                        Key = "secret:apiToken",
                                        CredentialKey = "project/dev/api-token",
                                        SecretSource = EnvironmentVariableSecretSource.WindowsCredentialStore,
                                        IsEnabled = true,
                                },
                        ],
        };

        await _fileService.SaveEnvironmentAsync(envPath, environment);
        var json = await File.ReadAllTextAsync(envPath);

        Assert.Contains("project/dev/api-token", json);
        Assert.DoesNotContain("super-secret-value", json);
        Assert.Contains("apiToken", json);
    }

    [Fact]
    public async Task CreateCollectionAsync_CreatesLinkedCollectionManifest()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");

        var collectionId = await _fileService.CreateCollectionAsync(apiRoot, "Second collection");
        var result = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });

        var collection = Assert.Single(result.Collections, collection => collection.Id == collectionId);
        Assert.Equal("Second collection", collection.Name);
        Assert.True(File.Exists(Path.Combine(apiRoot, "collections", "second-collection", "collection.json")));
    }

    [Fact]
    public async Task DeleteRequestAsync_RemovesRequestFileAndSidecars()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collectionPath = Path.Combine(apiRoot, "collections", "admin");
        Directory.CreateDirectory(collectionPath);
        await File.WriteAllTextAsync(Path.Combine(collectionPath, "users.swebreq.json"), """
            {
              "method": "GraphQl",
              "url": "{{graphqlUrl}}",
              "queryFile": "users.graphql"
            }
            """);

        var result = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });
        var collection = Assert.Single(result.Collections);
        var request = Assert.Single(collection.Nodes).Request!;
        request.GraphQlQuery = "query Users { users { id } }";
        request.Body.Mode = RequestBodyMode.Json;
        request.Body.RawContent = "{ \"sample\": true }";
        await _fileService.SaveRequestAsync(result.ApiRootPath, collection, request);

        Assert.True(File.Exists(Path.Combine(collectionPath, "users.swebreq.json")));
        Assert.True(File.Exists(Path.Combine(collectionPath, "users.graphql")));
        Assert.True(File.Exists(Path.Combine(collectionPath, "users.body.json")));

        await _fileService.DeleteRequestAsync(result.ApiRootPath, collection, request.Id);

        Assert.False(File.Exists(Path.Combine(collectionPath, "users.swebreq.json")));
        Assert.False(File.Exists(Path.Combine(collectionPath, "users.graphql")));
        Assert.False(File.Exists(Path.Combine(collectionPath, "users.body.json")));
    }

    [Fact]
    public async Task DeleteFolderAsync_WithNestedChildren_RemovesEverything()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collectionPath = Path.Combine(apiRoot, "collections", "orders");
        var folderPath = Path.Combine(collectionPath, "nested");
        var subFolderPath = Path.Combine(folderPath, "deep");
        Directory.CreateDirectory(subFolderPath);
        await File.WriteAllTextAsync(Path.Combine(folderPath, "list.swebreq.json"), """{ "method": "Get", "url": "/orders" }""");
        await File.WriteAllTextAsync(Path.Combine(subFolderPath, "get.swebreq.json"), """{ "method": "Get", "url": "/orders/1" }""");

        var result = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });
        var collection = Assert.Single(result.Collections);
        var folderNode = Assert.Single(collection.Nodes, n => n.Type == ApiCollectionNodeType.Folder);

        await _fileService.DeleteFolderAsync(result.ApiRootPath, collection, folderNode.Id);

        Assert.False(Directory.Exists(folderPath));
        Assert.False(Directory.Exists(subFolderPath));
        Assert.True(Directory.Exists(collectionPath));
    }

    [Fact]
    public async Task CreateFolderAsync_UnderCollectionRoot_CreatesDirectory()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collectionId = await _fileService.CreateCollectionAsync(apiRoot, "Orders");
        var result = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });
        var collection = Assert.Single(result.Collections, c => c.Id == collectionId);

        var folderDirectory = await _fileService.CreateFolderAsync(apiRoot, collection, parentFolder: null, "Admin Endpoints");

        Assert.True(Directory.Exists(folderDirectory));
        Assert.Equal("admin-endpoints", Path.GetFileName(folderDirectory));
    }

    [Fact]
    public async Task CreateFolderAsync_UnderExistingFolder_CreatesNestedDirectory()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collectionPath = Path.Combine(apiRoot, "collections", "orders");
        var folderPath = Path.Combine(collectionPath, "admin");
        Directory.CreateDirectory(folderPath);
        await File.WriteAllTextAsync(Path.Combine(folderPath, "list.swebreq.json"), """{ "method": "Get", "url": "/orders" }""");

        var result = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });
        var collection = Assert.Single(result.Collections);
        var parentFolder = Assert.Single(collection.Nodes, n => n.Type == ApiCollectionNodeType.Folder);

        var subFolderDirectory = await _fileService.CreateFolderAsync(apiRoot, collection, parentFolder, "Sub Folder");

        Assert.True(Directory.Exists(subFolderDirectory));
        Assert.Equal(folderPath, Path.GetDirectoryName(subFolderDirectory));
    }

    [Fact]
    public async Task RenameFolderAsync_RenamesDirectoryOnDisk()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collectionPath = Path.Combine(apiRoot, "collections", "orders");
        var folderPath = Path.Combine(collectionPath, "admin");
        Directory.CreateDirectory(folderPath);
        await File.WriteAllTextAsync(Path.Combine(folderPath, "list.swebreq.json"), """{ "method": "Get", "url": "/orders" }""");

        var result = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });
        var collection = Assert.Single(result.Collections);
        var folderNode = Assert.Single(collection.Nodes, n => n.Type == ApiCollectionNodeType.Folder);

        await _fileService.RenameFolderAsync(apiRoot, collection, folderNode.Id, "Renamed Admin");

        Assert.False(Directory.Exists(folderPath));
        Assert.True(Directory.Exists(Path.Combine(collectionPath, "renamed-admin")));
        Assert.True(File.Exists(Path.Combine(collectionPath, "renamed-admin", "list.swebreq.json")));
    }

    [Fact]
    public async Task MoveNodeAsync_Request_MovesFileToNewParentFolder()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collectionPath = Path.Combine(apiRoot, "collections", "orders");
        var targetFolderPath = Path.Combine(collectionPath, "archive");
        Directory.CreateDirectory(targetFolderPath);
        await File.WriteAllTextAsync(Path.Combine(collectionPath, "get-order.swebreq.json"), """{ "method": "Get", "url": "/orders/1" }""");

        var result = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });
        var collection = Assert.Single(result.Collections);
        var requestNode = Assert.Single(collection.Nodes, n => n.Type == ApiCollectionNodeType.Request);
        var targetFolderNode = Assert.Single(collection.Nodes, n => n.Type == ApiCollectionNodeType.Folder);

        await _fileService.MoveNodeAsync(apiRoot, collection, requestNode, targetFolderNode);

        Assert.False(File.Exists(Path.Combine(collectionPath, "get-order.swebreq.json")));
        Assert.True(File.Exists(Path.Combine(targetFolderPath, "get-order.swebreq.json")));
    }

    [Fact]
    public async Task MoveNodeAsync_Folder_MovesDirectoryToNewParentFolder()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collectionPath = Path.Combine(apiRoot, "collections", "orders");
        var sourceFolderPath = Path.Combine(collectionPath, "admin");
        var targetFolderPath = Path.Combine(collectionPath, "archive");
        Directory.CreateDirectory(sourceFolderPath);
        Directory.CreateDirectory(targetFolderPath);
        await File.WriteAllTextAsync(Path.Combine(sourceFolderPath, "list.swebreq.json"), """{ "method": "Get", "url": "/orders" }""");

        var result = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });
        var collection = Assert.Single(result.Collections);
        var sourceFolderNode = collection.Nodes.Single(n => n.Name == "Admin");
        var targetFolderNode = collection.Nodes.Single(n => n.Name == "Archive");

        await _fileService.MoveNodeAsync(apiRoot, collection, sourceFolderNode, targetFolderNode);

        Assert.False(Directory.Exists(sourceFolderPath));
        Assert.True(Directory.Exists(Path.Combine(targetFolderPath, "admin")));
        Assert.True(File.Exists(Path.Combine(targetFolderPath, "admin", "list.swebreq.json")));
    }

    [Fact]
    public async Task MoveNodeAsync_FolderIntoOwnDescendant_ThrowsInvalidOperationException()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collectionPath = Path.Combine(apiRoot, "collections", "orders");
        var parentFolderPath = Path.Combine(collectionPath, "admin");
        var childFolderPath = Path.Combine(parentFolderPath, "nested");
        Directory.CreateDirectory(childFolderPath);

        var result = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });
        var collection = Assert.Single(result.Collections);
        var parentFolderNode = collection.Nodes.Single(n => n.Name == "Admin");
        var childFolderNode = Assert.Single(parentFolderNode.Children);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fileService.MoveNodeAsync(apiRoot, collection, parentFolderNode, childFolderNode));
    }

    [Fact]
    public async Task DeleteCollectionDirectoryAsync_RemovesCollectionDirectory()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collectionId = await _fileService.CreateCollectionAsync(apiRoot, "Temp Collection");
        var result = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });
        var collection = Assert.Single(result.Collections, c => c.Id == collectionId);

        await _fileService.DeleteCollectionDirectoryAsync(apiRoot, collection);

        Assert.False(Directory.Exists(Path.Combine(apiRoot, "collections", "temp-collection")));
    }

    [Fact]
    public async Task RenameCollectionDirectoryAsync_RenamesDirectoryAndManifest()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collectionId = await _fileService.CreateCollectionAsync(apiRoot, "Temp Collection");
        var result = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });
        var collection = Assert.Single(result.Collections, c => c.Id == collectionId);

        await _fileService.RenameCollectionDirectoryAsync(apiRoot, collection, "Renamed Collection");

        var newDirectory = Path.Combine(apiRoot, "collections", "renamed-collection");
        Assert.True(Directory.Exists(newDirectory));
        var manifestJson = await File.ReadAllTextAsync(Path.Combine(newDirectory, "collection.json"));
        Assert.Contains("Renamed Collection", manifestJson);
    }

    [Fact]
    public async Task LoadRootAsync_EnvironmentFile_LoadsGeneratedVariables()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var envPath = Path.Combine(apiRoot, "environments", "dev.swebenv.json");
        await File.WriteAllTextAsync(envPath, """
            {
              "name": "dev",
              "generatedVariables": {
                "age": {
                  "kind": "Integer",
                  "minInt": 10,
                  "maxInt": 20
                }
              }
            }
            """);

        var result = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });

        var variable = Assert.Single(Assert.Single(result.Environments).Variables);
        Assert.Equal("age", variable.Key);
        Assert.Equal(EnvironmentVariableSecretSource.Generated, variable.SecretSource);
        Assert.Equal(VariableGeneratorKind.Integer, variable.Generator?.Kind);
        Assert.Equal(10, variable.Generator?.MinInt);
        Assert.Equal(20, variable.Generator?.MaxInt);
    }

    [Fact]
    public async Task SaveEnvironmentAsync_WritesGeneratedDefinitionWithoutSampleValue()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var envPath = Path.Combine(apiRoot, "environments", "dev.swebenv.json");
        var environment = new ApiEnvironment
        {
            Id = "dev",
            Name = "dev",
            Variables =
            [
                new EnvironmentVariable
                {
                    Key = "firstName",
                    SecretSource = EnvironmentVariableSecretSource.Generated,
                    Generator = new VariableGeneratorDefinition
                    {
                        Kind = VariableGeneratorKind.Faker,
                        FakerCategory = "person.firstName",
                    },
                    IsEnabled = true,
                },
            ],
        };

        await _fileService.SaveEnvironmentAsync(envPath, environment);
        var json = await File.ReadAllTextAsync(envPath);

        Assert.Contains("generatedVariables", json);
        Assert.Contains("person.firstName", json);
        Assert.DoesNotContain("variables\": {\n    \"firstName", json);
    }

    [Fact]
    public async Task LinkedGitService_NonRepository_ReturnsNotGitRepository()
    {
        Directory.CreateDirectory(_root);
        var status = await new LinkedGitService().GetStatusAsync(_root, _root);

        Assert.False(status.IsGitRepository);
    }

    [Fact]
    public async Task CommitApiFilesAsync_CommitsOnlyFilesUnderApiRoot()
    {
        if (!await IsGitAvailableAsync())
        {
            return;
        }

        Directory.CreateDirectory(_root);
        await RunGitAsync(_root, "init");
        await RunGitAsync(_root, "config user.email test@example.com");
        await RunGitAsync(_root, "config user.name SwebKit Test");
        await File.WriteAllTextAsync(Path.Combine(_root, "README.md"), "initial");
        await RunGitAsync(_root, "add README.md");
        await RunGitAsync(_root, "commit -m initial");

        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        await File.WriteAllTextAsync(Path.Combine(_root, "outside.txt"), "do not commit");

        var git = new LinkedGitService();
        var commitResult = await git.CommitApiFilesAsync(_root, apiRoot, "api collection changes");
        var status = await RunGitWithOutputAsync(_root, "status --porcelain --untracked-files=all");

        Assert.True(commitResult.IsSuccess, commitResult.ErrorMessage);
        Assert.Contains("?? outside.txt", status);
        Assert.DoesNotContain(".swebkit-api", status);
    }

    [Fact]
    public async Task StageUnstageAndRevertFileAsync_AffectOnlyLinkedApiFile()
    {
        if (!await IsGitAvailableAsync())
        {
            return;
        }

        Directory.CreateDirectory(_root);
        await RunGitAsync(_root, "init");
        await RunGitAsync(_root, "config user.email test@example.com");
        await RunGitAsync(_root, "config user.name SwebKit Test");
        await File.WriteAllTextAsync(Path.Combine(_root, "README.md"), "initial");
        await RunGitAsync(_root, "add README.md");
        await RunGitAsync(_root, "commit -m initial");

        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var requestPath = Path.Combine(apiRoot, "collections", "orders", "get-order.swebreq.json");
        Directory.CreateDirectory(Path.GetDirectoryName(requestPath)!);
        await File.WriteAllTextAsync(requestPath, "{ \"method\": \"Get\", \"url\": \"/orders/1\" }");

        var git = new LinkedGitService();
        var relativePath = ".swebkit-api/collections/orders/get-order.swebreq.json";

        var stageResult = await git.StageFileAsync(_root, apiRoot, relativePath);
        Assert.True(stageResult.IsSuccess, stageResult.ErrorMessage);
        var stagedStatus = await git.GetStatusAsync(_root, apiRoot);
        Assert.True(stagedStatus.ChangedFileDetails.Single(file => file.Path == relativePath).IsStaged);

        var unstageResult = await git.UnstageFileAsync(_root, apiRoot, relativePath);
        Assert.True(unstageResult.IsSuccess, unstageResult.ErrorMessage);
        var unstagedStatus = await git.GetStatusAsync(_root, apiRoot);
        Assert.False(unstagedStatus.ChangedFileDetails.Single(file => file.Path == relativePath).IsStaged);

        var revertResult = await git.RevertFileAsync(_root, apiRoot, relativePath);
        Assert.True(revertResult.IsSuccess, revertResult.ErrorMessage);
        Assert.False(File.Exists(requestPath));
    }

    [Fact]
    public async Task CommitStagedApiFilesAsync_RejectsExternalStagedFiles()
    {
        if (!await IsGitAvailableAsync())
        {
            return;
        }

        Directory.CreateDirectory(_root);
        await RunGitAsync(_root, "init");
        await RunGitAsync(_root, "config user.email test@example.com");
        await RunGitAsync(_root, "config user.name SwebKit Test");
        await File.WriteAllTextAsync(Path.Combine(_root, "README.md"), "initial");
        await RunGitAsync(_root, "add README.md");
        await RunGitAsync(_root, "commit -m initial");

        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var requestPath = Path.Combine(apiRoot, "collections", "orders", "get-order.swebreq.json");
        Directory.CreateDirectory(Path.GetDirectoryName(requestPath)!);
        await File.WriteAllTextAsync(requestPath, "{ \"method\": \"Get\", \"url\": \"/orders/1\" }");
        await File.WriteAllTextAsync(Path.Combine(_root, "outside.txt"), "outside");
        await RunGitAsync(_root, "add .swebkit-api/collections/orders/get-order.swebreq.json outside.txt");

        var result = await new LinkedGitService().CommitStagedApiFilesAsync(_root, apiRoot, "api changes");

        Assert.False(result.IsSuccess);
        Assert.Contains("non-API", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateBranchAsync_RejectsUnsafeBranchName()
    {
        var result = await new LinkedGitService().CreateBranchAsync(_root, "bad branch;name");

        Assert.False(result.IsSuccess);
        Assert.Contains("unsupported", result.ErrorMessage);
    }

    [Fact]
    public async Task GetBranchesAsync_ReturnsCurrentBranch()
    {
        if (!await IsGitAvailableAsync())
        {
            return;
        }

        Directory.CreateDirectory(_root);
        await RunGitAsync(_root, "init");
        await RunGitAsync(_root, "config user.email test@example.com");
        await RunGitAsync(_root, "config user.name SwebKit Test");
        await File.WriteAllTextAsync(Path.Combine(_root, "README.md"), "initial");
        await RunGitAsync(_root, "add README.md");
        await RunGitAsync(_root, "commit -m initial");
        await RunGitAsync(_root, "checkout -b sw/test/ref");

        var branches = await new LinkedGitService().GetBranchesAsync(_root);

        Assert.Contains(branches, branch => branch.Name == "sw/test/ref" && branch.IsCurrent);
    }

    [Fact]
    public async Task GetFileDiffAsync_ForModifiedApiFile_ReturnsOriginalAndCurrentContent()
    {
        if (!await IsGitAvailableAsync())
        {
            return;
        }

        Directory.CreateDirectory(_root);
        await RunGitAsync(_root, "init");
        await RunGitAsync(_root, "config user.email test@example.com");
        await RunGitAsync(_root, "config user.name SwebKit Test");

        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var requestPath = Path.Combine(apiRoot, "collections", "orders", "get-order.swebreq.json");
        Directory.CreateDirectory(Path.GetDirectoryName(requestPath)!);
        await File.WriteAllTextAsync(requestPath, "{ \"method\": \"Get\", \"url\": \"/orders/1\" }");
        await RunGitAsync(_root, "add .swebkit-api/collections/orders/get-order.swebreq.json");
        await RunGitAsync(_root, "commit -m initial");

        await File.WriteAllTextAsync(requestPath, "{ \"method\": \"Get\", \"url\": \"/orders/2\" }");

        var diff = await new LinkedGitService().GetFileDiffAsync(_root, apiRoot, ".swebkit-api/collections/orders/get-order.swebreq.json");

        Assert.True(diff.IsSuccess, diff.ErrorMessage);
        Assert.Contains("/orders/1", diff.OriginalContent);
        Assert.Contains("/orders/2", diff.CurrentContent);
    }

    [Fact]
    public async Task GetFileDiffAsync_ForOutsideFile_ReturnsError()
    {
        if (!await IsGitAvailableAsync())
        {
            return;
        }

        Directory.CreateDirectory(_root);
        await RunGitAsync(_root, "init");
        await RunGitAsync(_root, "config user.email test@example.com");
        await RunGitAsync(_root, "config user.name SwebKit Test");
        await File.WriteAllTextAsync(Path.Combine(_root, "README.md"), "initial");
        await RunGitAsync(_root, "add README.md");
        await RunGitAsync(_root, "commit -m initial");
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        await File.WriteAllTextAsync(Path.Combine(_root, "outside.txt"), "outside");

        var diff = await new LinkedGitService().GetFileDiffAsync(_root, apiRoot, "outside.txt");

        Assert.False(diff.IsSuccess);
        Assert.Contains("changed linked API file", diff.ErrorMessage);
    }

    [Fact]
    public async Task GetRemoteCompareUrlAsync_GitHubRemote_ReturnsCompareUrl()
    {
        if (!await IsGitAvailableAsync())
        {
            return;
        }

        Directory.CreateDirectory(_root);
        await RunGitAsync(_root, "init");
        await RunGitAsync(_root, "checkout -b feature/test");
        await RunGitAsync(_root, "remote add origin https://github.com/example/project.git");

        var url = await new LinkedGitService().GetRemoteCompareUrlAsync(_root);

        Assert.Equal("https://github.com/example/project/compare/feature%2Ftest?expand=1", url);
    }

    // ── Sibling ordering persistence (drag-and-drop reorder) ───────────────────

    [Fact]
    public async Task WriteThenLoad_PreservesTopLevelNodeOrder()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collection = new ApiCollection
        {
            Id = "c1",
            Name = "Ordered",
            Nodes =
            [
                Request("Beta"),
                Folder("Zeta"),
                Request("Alpha"),
            ],
        };

        await _fileService.WriteCollectionToLinkedRootAsync(apiRoot, collection);
        var result = await _fileService.LoadRootAsync(new LinkedCollectionRootConfig { Id = "r1", Name = "Project APIs", Path = _root });

        var loaded = Assert.Single(result.Collections);
        // Import order is preserved verbatim — not re-sorted folders-first / alphabetical.
        Assert.Equal(new[] { "Beta", "Zeta", "Alpha" }, loaded.Nodes.Select(n => n.Name));
    }

    [Fact]
    public async Task MoveNodeAsync_SameParentReorder_PersistsAcrossReload()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collection = new ApiCollection
        {
            Id = "c1",
            Name = "Ordered",
            Nodes = [Request("Alpha"), Request("Beta"), Request("Gamma")],
        };
        await _fileService.WriteCollectionToLinkedRootAsync(apiRoot, collection);

        var loaded = Assert.Single((await _fileService.LoadRootAsync(Config())).Collections);
        var gamma = loaded.Nodes.Single(n => n.Name == "Gamma");

        // Mimic the page: reorder the in-memory tree first, then persist the move.
        loaded.Nodes.Remove(gamma);
        loaded.Nodes.Insert(0, gamma);
        await _fileService.MoveNodeAsync(apiRoot, loaded, gamma, newParentFolder: null);

        var reloaded = Assert.Single((await _fileService.LoadRootAsync(Config())).Collections);
        Assert.Equal(new[] { "Gamma", "Alpha", "Beta" }, reloaded.Nodes.Select(n => n.Name));
    }

    [Fact]
    public async Task MoveNodeAsync_CrossParentReorder_PersistsDestinationOrder()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collection = new ApiCollection
        {
            Id = "c1",
            Name = "Ordered",
            Nodes =
            [
                Request("Loose"),
                Folder("Bucket", Request("First"), Request("Second")),
            ],
        };
        await _fileService.WriteCollectionToLinkedRootAsync(apiRoot, collection);

        var loaded = Assert.Single((await _fileService.LoadRootAsync(Config())).Collections);
        var loose = loaded.Nodes.Single(n => n.Name == "Loose");
        var bucket = loaded.Nodes.Single(n => n.Name == "Bucket");

        // Move "Loose" into "Bucket" and place it between First and Second.
        loaded.Nodes.Remove(loose);
        bucket.Children.Insert(1, loose);
        await _fileService.MoveNodeAsync(apiRoot, loaded, loose, bucket);

        var reloaded = Assert.Single((await _fileService.LoadRootAsync(Config())).Collections);
        var reloadedBucket = reloaded.Nodes.Single(n => n.Name == "Bucket");
        Assert.Equal(new[] { "First", "Loose", "Second" }, reloadedBucket.Children.Select(n => n.Name));
    }

    [Fact]
    public async Task RenameFolderAsync_KeepsPositionInPersistedOrder()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collection = new ApiCollection
        {
            Id = "c1",
            Name = "Ordered",
            Nodes = [Folder("Aardvark", Request("Ping")), Folder("Beluga", Request("Pong"))],
        };
        await _fileService.WriteCollectionToLinkedRootAsync(apiRoot, collection);

        var loaded = Assert.Single((await _fileService.LoadRootAsync(Config())).Collections);
        var aardvark = loaded.Nodes.Single(n => n.Name == "Aardvark");
        await _fileService.RenameFolderAsync(apiRoot, loaded, aardvark.Id, "Zebra");

        var reloaded = Assert.Single((await _fileService.LoadRootAsync(Config())).Collections);
        // "Zebra" keeps Aardvark's first slot rather than jumping to the end alphabetically.
        Assert.Equal(new[] { "Zebra", "Beluga" }, reloaded.Nodes.Select(n => n.Name));
    }

    // ── Collection-scoped environments (linked repo) ───────────────────────────

    [Fact]
    public async Task WriteEnvironmentToCollection_LoadsScopedToThatCollection()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collection = new ApiCollection { Id = "c1", Name = "Orders", Nodes = [Request("List")] };
        var collectionDir = await _fileService.WriteCollectionToLinkedRootAsync(apiRoot, collection);

        var scoped = new ApiEnvironment { Id = "e1", Name = "DEV", Variables = [new EnvironmentVariable { Key = "BASE", Value = "x", IsEnabled = true }] };
        var global = new ApiEnvironment { Id = "e2", Name = "Shared", Variables = [] };
        await _fileService.WriteEnvironmentToCollectionAsync(apiRoot, collectionDir, scoped);
        await _fileService.WriteEnvironmentToLinkedRootAsync(apiRoot, global);

        var result = await _fileService.LoadRootAsync(Config());
        var loadedCollection = Assert.Single(result.Collections);

        var dev = Assert.Single(result.Environments, e => e.Name == "DEV");
        Assert.Equal(loadedCollection.Id, dev.CollectionId);
        var shared = Assert.Single(result.Environments, e => e.Name == "Shared");
        Assert.Null(shared.CollectionId);

        // The environments/ subfolder must not appear as a request folder node in the collection tree.
        Assert.DoesNotContain(loadedCollection.Nodes, n => n.Type == ApiCollectionNodeType.Folder && n.Name == "Environments");
    }

    [Fact]
    public async Task WriteEnvironmentToCollection_StoresUnderCollectionEnvironmentsFolder()
    {
        var apiRoot = await _fileService.EnsureRootAsync(_root, "Project APIs");
        var collection = new ApiCollection { Id = "c1", Name = "Orders", Nodes = [Request("List")] };
        var collectionDir = await _fileService.WriteCollectionToLinkedRootAsync(apiRoot, collection);

        await _fileService.WriteEnvironmentToCollectionAsync(apiRoot, collectionDir,
            new ApiEnvironment { Id = "e1", Name = "DEV", Variables = [] });

        Assert.True(File.Exists(Path.Combine(collectionDir, "environments", "dev.swebenv.json")));
    }

    private LinkedCollectionRootConfig Config() => new() { Id = "r1", Name = "Project APIs", Path = _root };

    private static ApiCollectionNode Request(string name) => new()
    {
        Id = name,
        Type = ApiCollectionNodeType.Request,
        Name = name,
        Request = new HttpRequestEntry { Id = name, Name = name, Method = ApiRequestMethod.Get, Url = $"/{name}" },
    };

    private static ApiCollectionNode Folder(string name, params ApiCollectionNode[] children) => new()
    {
        Id = name,
        Type = ApiCollectionNodeType.Folder,
        Name = name,
        Children = [.. children],
    };

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static async Task<bool> IsGitAvailableAsync()
    {
        try
        {
            await RunGitWithOutputAsync(Directory.GetCurrentDirectory(), "--version");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task RunGitAsync(string workingDirectory, string arguments)
    {
        _ = await RunGitWithOutputAsync(workingDirectory, arguments);
    }

    private static async Task<string> RunGitWithOutputAsync(string workingDirectory, string arguments)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        Assert.NotNull(process);
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
        return output;
    }
}
