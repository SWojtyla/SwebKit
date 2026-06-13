using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SwebKit.Core.Domain;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Services;

public sealed partial class LinkedCollectionFileService(LinkedGitService gitService)
{
    public const string RootFolderName = ".swebkit-api";
    public const string ManifestFileName = "swebkit.json";
    public const string RequestFileExtension = ".swebreq.json";

    private static readonly JsonSerializerOptions Options = new(SwebKitJsonOptions.Indented)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<string> EnsureRootAsync(string configuredPath, string displayName, CancellationToken cancellationToken = default)
    {
        var apiRootPath = ResolveApiRootPathForCreate(configuredPath);
        Directory.CreateDirectory(apiRootPath);
        Directory.CreateDirectory(Path.Combine(apiRootPath, "collections"));
        Directory.CreateDirectory(Path.Combine(apiRootPath, "environments"));

        var manifestPath = Path.Combine(apiRootPath, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            var manifest = new SwebKitApiRootManifest
            {
                Name = string.IsNullOrWhiteSpace(displayName)
                    ? TitleFromFileName(Path.GetFileName(Path.GetDirectoryName(apiRootPath) ?? "Linked APIs"))
                    : displayName.Trim(),
            };
            await WriteJsonAtomicAsync(manifestPath, manifest, cancellationToken);
        }

        return apiRootPath;
    }

    public async Task<LinkedCollectionRootLoadResult> LoadRootAsync(LinkedCollectionRootConfig config, CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<string>();
        var apiRootPath = ResolveApiRootPath(config.Path);

        if (!Directory.Exists(apiRootPath))
        {
            diagnostics.Add($"Linked API root not found: {apiRootPath}");
            return BuildResult(config, apiRootPath, config.Name, [], [], [], [], diagnostics, await gitService.GetStatusAsync(config.Path, apiRootPath, cancellationToken));
        }

        var manifestPath = Path.Combine(apiRootPath, ManifestFileName);
        var manifest = await ReadJsonOrDefaultAsync<SwebKitApiRootManifest>(manifestPath, diagnostics, cancellationToken) ?? new SwebKitApiRootManifest { Name = config.Name };
        var collectionsPath = Path.Combine(apiRootPath, "collections");
        var environmentsPath = Path.Combine(apiRootPath, "environments");
        var collections = new List<ApiCollection>();
        var environments = new List<ApiEnvironment>();
        var requestFiles = new List<LinkedRequestFileState>();
        var environmentFiles = new List<LinkedEnvironmentFileState>();

        if (Directory.Exists(collectionsPath))
        {
            foreach (var collectionDirectory in Directory.GetDirectories(collectionsPath).OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
            {
                collections.Add(await ReadCollectionAsync(collectionDirectory, requestFiles, diagnostics, cancellationToken));
            }

            foreach (var requestFile in Directory.GetFiles(collectionsPath, $"*{RequestFileExtension}", SearchOption.TopDirectoryOnly).OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
            {
                var rootCollection = collections.FirstOrDefault(c => c.Id == "root") ?? CreateRootCollection(manifest.Name, apiRootPath);
                if (!collections.Contains(rootCollection))
                {
                    collections.Insert(0, rootCollection);
                }

                rootCollection.Nodes.Add(await ReadRequestNodeAsync(requestFile, requestFiles, diagnostics, cancellationToken));
            }
        }
        else
        {
            diagnostics.Add($"Collections folder not found: {collectionsPath}");
        }

        if (Directory.Exists(environmentsPath))
        {
            foreach (var environmentFile in Directory.GetFiles(environmentsPath, "*.swebenv.json", SearchOption.TopDirectoryOnly).OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
            {
                var environment = await ReadEnvironmentAsync(environmentFile, diagnostics, cancellationToken);
                environments.Add(environment);
                environmentFiles.Add(new LinkedEnvironmentFileState
                {
                    EnvironmentId = environment.Id,
                    EnvironmentFilePath = environmentFile,
                });
            }
        }

        var gitStatus = await gitService.GetStatusAsync(config.Path, apiRootPath, cancellationToken);
        return BuildResult(config, apiRootPath, string.IsNullOrWhiteSpace(manifest.Name) ? config.Name : manifest.Name, collections, environments, requestFiles, environmentFiles, diagnostics, gitStatus);
    }

    public async Task SaveEnvironmentAsync(string environmentFilePath, ApiEnvironment environment, CancellationToken cancellationToken = default)
    {
        var file = SwebKitEnvironmentFile.FromEnvironment(environment);
        await WriteJsonAtomicAsync(environmentFilePath, file, cancellationToken);
    }

    public async Task<string> CreateCollectionAsync(string apiRootPath, string collectionName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            throw new ArgumentException("Collection name is required.", nameof(collectionName));
        }

        var collectionsPath = Path.Combine(apiRootPath, "collections");
        Directory.CreateDirectory(collectionsPath);

        var collectionDirectory = GetUniqueCollectionDirectory(collectionsPath, Slugify(collectionName));
        Directory.CreateDirectory(collectionDirectory);

        var manifest = new SwebKitCollectionManifest { Name = collectionName.Trim() };
        await WriteJsonAtomicAsync(Path.Combine(collectionDirectory, "collection.json"), manifest, cancellationToken);
        return StableId(collectionDirectory);
    }

    public async Task<LinkedRequestSaveResult> SaveRequestAsync(
        string apiRootPath,
        ApiCollection collection,
        HttpRequestEntry request,
        string? expectedContentStamp = null,
        CancellationToken cancellationToken = default)
    {
        var requestPath = GetRequestFilePath(apiRootPath, collection, request);
        Directory.CreateDirectory(Path.GetDirectoryName(requestPath)!);

        if (!string.IsNullOrWhiteSpace(expectedContentStamp) && File.Exists(requestPath))
        {
            var currentContentStamp = await ComputeRequestContentStampAsync(requestPath, cancellationToken);
            if (!string.Equals(expectedContentStamp, currentContentStamp, StringComparison.Ordinal))
            {
                return LinkedRequestSaveResult.Conflict(requestPath, currentContentStamp);
            }
        }

        var requestFile = SwebKitRequestFile.FromRequest(request, requestPath);
        await WriteJsonAtomicAsync(requestPath, requestFile, cancellationToken);

        if (!string.IsNullOrWhiteSpace(requestFile.Body?.JsonFile) && !string.IsNullOrWhiteSpace(request.Body.RawContent))
        {
            await WriteTextAtomicAsync(Path.Combine(Path.GetDirectoryName(requestPath)!, requestFile.Body.JsonFile), request.Body.RawContent, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(requestFile.QueryFile) && !string.IsNullOrWhiteSpace(request.GraphQlQuery))
        {
            await WriteTextAtomicAsync(Path.Combine(Path.GetDirectoryName(requestPath)!, requestFile.QueryFile), request.GraphQlQuery, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(requestFile.VariablesFile) && !string.IsNullOrWhiteSpace(request.GraphQlVariables))
        {
            await WriteTextAtomicAsync(Path.Combine(Path.GetDirectoryName(requestPath)!, requestFile.VariablesFile), request.GraphQlVariables, cancellationToken);
        }

        return LinkedRequestSaveResult.Success(requestPath, await ComputeRequestContentStampAsync(requestPath, cancellationToken));
    }

    private static LinkedCollectionRootLoadResult BuildResult(
        LinkedCollectionRootConfig config,
        string apiRootPath,
        string displayName,
        IReadOnlyList<ApiCollection> collections,
        IReadOnlyList<ApiEnvironment> environments,
        IReadOnlyList<LinkedRequestFileState> requestFiles,
        IReadOnlyList<LinkedEnvironmentFileState> environmentFiles,
        IReadOnlyList<string> diagnostics,
        LinkedGitStatus gitStatus) => new()
        {
            Config = config,
            ApiRootPath = apiRootPath,
            DisplayName = displayName,
            Collections = collections,
            Environments = environments,
            RequestFiles = requestFiles,
            EnvironmentFiles = environmentFiles,
            Diagnostics = diagnostics,
            GitStatus = gitStatus,
        };

    private static string ResolveApiRootPath(string configuredPath)
    {
        var fullPath = Path.GetFullPath(configuredPath);
        if (string.Equals(Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), RootFolderName, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        var candidate = Path.Combine(fullPath, RootFolderName);
        return Directory.Exists(candidate) || File.Exists(Path.Combine(candidate, ManifestFileName))
            ? candidate
            : fullPath;
    }

    private static string ResolveApiRootPathForCreate(string configuredPath)
    {
        var fullPath = Path.GetFullPath(configuredPath);
        if (string.Equals(Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), RootFolderName, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return Path.Combine(fullPath, RootFolderName);
    }

    private static async Task<ApiCollection> ReadCollectionAsync(
        string collectionDirectory,
        List<LinkedRequestFileState> requestFiles,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var collectionManifestPath = Path.Combine(collectionDirectory, "collection.json");
        var manifest = await ReadJsonOrDefaultAsync<SwebKitCollectionManifest>(collectionManifestPath, diagnostics, cancellationToken);
        var collection = new ApiCollection
        {
            Id = StableId(collectionDirectory),
            Name = manifest?.Name ?? TitleFromFileName(Path.GetFileName(collectionDirectory)),
            Variables = manifest?.Variables ?? [],
            DefaultAuth = manifest?.DefaultAuth,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        if (manifest?.GeneratedVariables is not null)
        {
            collection.Variables.AddRange(manifest.GeneratedVariables.Select(pair => new CollectionVariable
            {
                Key = pair.Key,
                Generator = pair.Value,
                IsEnabled = true,
            }));
        }

        collection.Nodes.AddRange(await ReadNodesAsync(collectionDirectory, requestFiles, diagnostics, cancellationToken));
        return collection;
    }

    private static ApiCollection CreateRootCollection(string name, string apiRootPath) => new()
    {
        Id = "root",
        Name = string.IsNullOrWhiteSpace(name) ? TitleFromFileName(Path.GetFileName(Path.GetDirectoryName(apiRootPath) ?? "Linked APIs")) : name,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static async Task<List<ApiCollectionNode>> ReadNodesAsync(
        string directory,
        List<LinkedRequestFileState> requestFiles,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var nodes = new List<ApiCollectionNode>();

        foreach (var childDirectory in Directory.GetDirectories(directory).OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
        {
            nodes.Add(new ApiCollectionNode
            {
                Id = StableId(childDirectory),
                Type = ApiCollectionNodeType.Folder,
                Name = TitleFromFileName(Path.GetFileName(childDirectory)),
                IsExpanded = true,
                Children = await ReadNodesAsync(childDirectory, requestFiles, diagnostics, cancellationToken),
            });
        }

        foreach (var requestFile in Directory.GetFiles(directory, $"*{RequestFileExtension}", SearchOption.TopDirectoryOnly).OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
        {
            nodes.Add(await ReadRequestNodeAsync(requestFile, requestFiles, diagnostics, cancellationToken));
        }

        return nodes;
    }

    private static async Task<ApiCollectionNode> ReadRequestNodeAsync(
        string requestFile,
        List<LinkedRequestFileState> requestFiles,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var dto = await ReadJsonOrDefaultAsync<SwebKitRequestFile>(requestFile, diagnostics, cancellationToken) ?? new SwebKitRequestFile();
        var requestDirectory = Path.GetDirectoryName(requestFile)!;
        var request = dto.ToRequest(requestFile);

        if (!string.IsNullOrWhiteSpace(dto.Body?.JsonFile))
        {
            request.Body.Mode = RequestBodyMode.Json;
            request.Body.RawContent = await ReadSidecarAsync(requestDirectory, dto.Body.JsonFile, diagnostics, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(dto.QueryFile))
        {
            request.GraphQlQuery = await ReadSidecarAsync(requestDirectory, dto.QueryFile, diagnostics, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(dto.VariablesFile))
        {
            request.GraphQlVariables = await ReadSidecarAsync(requestDirectory, dto.VariablesFile, diagnostics, cancellationToken);
        }

        requestFiles.Add(new LinkedRequestFileState
        {
            RequestId = request.Id,
            RequestFilePath = requestFile,
            ContentStamp = await ComputeRequestContentStampAsync(requestFile, cancellationToken),
        });

        return new ApiCollectionNode
        {
            Id = StableId(requestFile),
            Type = ApiCollectionNodeType.Request,
            Name = request.Name,
            Request = request,
        };
    }

    private static async Task<ApiEnvironment> ReadEnvironmentAsync(string environmentFile, List<string> diagnostics, CancellationToken cancellationToken)
    {
        var dto = await ReadJsonOrDefaultAsync<SwebKitEnvironmentFile>(environmentFile, diagnostics, cancellationToken) ?? new SwebKitEnvironmentFile();
        return dto.ToEnvironment(environmentFile);
    }

    private static async Task<T?> ReadJsonOrDefaultAsync<T>(string path, List<string> diagnostics, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add($"Could not read '{path}': {ex.Message}");
            return default;
        }
    }

    private static async Task<string?> ReadSidecarAsync(string requestDirectory, string fileName, List<string> diagnostics, CancellationToken cancellationToken)
    {
        var path = Path.Combine(requestDirectory, fileName);
        try
        {
            return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add($"Could not read sidecar '{path}': {ex.Message}");
            return null;
        }
    }

    private static string GetRequestFilePath(string apiRootPath, ApiCollection collection, HttpRequestEntry request)
    {
        var collectionsPath = Path.Combine(apiRootPath, "collections");
        var collectionDirectory = Directory.GetDirectories(collectionsPath, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(p => StableId(p) == collection.Id)
            ?? Path.Combine(collectionsPath, Slugify(collection.Name));
        return FindRequestFile(collectionDirectory, request.Id) ?? Path.Combine(collectionDirectory, $"{Slugify(request.Name)}{RequestFileExtension}");
    }

    private static string? FindRequestFile(string directory, string requestId)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (var file in Directory.GetFiles(directory, $"*{RequestFileExtension}", SearchOption.AllDirectories))
        {
            if (StableId(file) == requestId)
            {
                return file;
            }
        }

        return null;
    }

    private static async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, Options);
        await WriteTextAtomicAsync(path, json, cancellationToken);
    }

    private static async Task WriteTextAtomicAsync(string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, content, cancellationToken);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static async Task<string> ComputeRequestContentStampAsync(string requestPath, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await AppendFileToHashAsync(hash, requestPath, cancellationToken);

        try
        {
            var json = await File.ReadAllTextAsync(requestPath, cancellationToken);
            var requestFile = JsonSerializer.Deserialize<SwebKitRequestFile>(json, Options);
            var directory = Path.GetDirectoryName(requestPath)!;
            foreach (var sidecar in requestFile?.GetSidecarFiles() ?? [])
            {
                var sidecarPath = Path.Combine(directory, sidecar);
                if (File.Exists(sidecarPath))
                {
                    await AppendFileToHashAsync(hash, sidecarPath, cancellationToken);
                }
            }
        }
        catch (JsonException)
        {
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task AppendFileToHashAsync(IncrementalHash hash, string path, CancellationToken cancellationToken)
    {
        var pathBytes = Encoding.UTF8.GetBytes(Path.GetFileName(path));
        hash.AppendData(pathBytes);
        hash.AppendData([0]);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        hash.AppendData(bytes);
        hash.AppendData([0]);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string StableId(string path) => Slugify(Path.GetRelativePath(Path.GetPathRoot(Path.GetFullPath(path)) ?? string.Empty, Path.GetFullPath(path)));

    private static string TitleFromFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName.Replace(RequestFileExtension, string.Empty, StringComparison.OrdinalIgnoreCase));
        return string.Join(' ', name.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static string Slugify(string value)
    {
        var lower = value.Replace('\\', '/').ToLowerInvariant();
        var slug = NonSlugCharacterRegex().Replace(lower, "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "item" : slug;
    }

    private static string GetUniqueCollectionDirectory(string collectionsPath, string slug)
    {
        var candidate = Path.Combine(collectionsPath, slug);
        if (!Directory.Exists(candidate))
        {
            return candidate;
        }

        for (var index = 2; ; index++)
        {
            candidate = Path.Combine(collectionsPath, $"{slug}-{index}");
            if (!Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSlugCharacterRegex();

    private sealed class SwebKitRequestFile
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public ApiRequestMethod Method { get; set; } = ApiRequestMethod.Get;
        public string Url { get; set; } = string.Empty;
        public object? Headers { get; set; }
        public object? Query { get; set; }
        public LinkedRequestBodyFile? Body { get; set; }
        public AuthConfig? Auth { get; set; }
        public List<CaptureRule> CaptureRules { get; set; } = [];
        public List<ResponseExample> ResponseExamples { get; set; } = [];
        public string? QueryFile { get; set; }
        public string? VariablesFile { get; set; }
        public string? GraphQlQuery { get; set; }
        public string? GraphQlVariables { get; set; }

        public HttpRequestEntry ToRequest(string path)
        {
            var request = new HttpRequestEntry
            {
                Id = string.IsNullOrWhiteSpace(Id) ? StableId(path) : Id,
                Name = string.IsNullOrWhiteSpace(Name) ? TitleFromFileName(Path.GetFileName(path)) : Name,
                Method = Method,
                Url = Url,
                Headers = ParsePairs(Headers),
                QueryParams = ParsePairs(Query),
                Auth = Auth,
                CaptureRules = CaptureRules,
                ResponseExamples = ResponseExamples,
                GraphQlQuery = GraphQlQuery,
                GraphQlVariables = GraphQlVariables,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            if (Body?.Json is not null)
            {
                request.Body.Mode = RequestBodyMode.Json;
                request.Body.RawContent = JsonSerializer.Serialize(Body.Json, Options);
            }
            else if (!string.IsNullOrWhiteSpace(Body?.Text))
            {
                request.Body.Mode = RequestBodyMode.Text;
                request.Body.RawContent = Body.Text;
            }
            else if (!string.IsNullOrWhiteSpace(Body?.Xml))
            {
                request.Body.Mode = RequestBodyMode.Xml;
                request.Body.RawContent = Body.Xml;
            }

            return request;
        }

        public static SwebKitRequestFile FromRequest(HttpRequestEntry request, string requestPath)
        {
            var file = new SwebKitRequestFile
            {
                Method = request.Method,
                Url = request.Url,
                Headers = request.Headers.Count == 0 ? null : request.Headers,
                Query = request.QueryParams.Count == 0 ? null : request.QueryParams,
                Body = LinkedRequestBodyFile.FromRequest(request, requestPath),
                Auth = request.Auth,
                CaptureRules = request.CaptureRules,
                ResponseExamples = request.ResponseExamples,
                QueryFile = request.Method == ApiRequestMethod.GraphQl && !string.IsNullOrWhiteSpace(request.GraphQlQuery)
                    ? $"{GetRequestBaseName(requestPath)}.graphql"
                    : null,
                VariablesFile = request.Method == ApiRequestMethod.GraphQl && !string.IsNullOrWhiteSpace(request.GraphQlVariables)
                    ? $"{GetRequestBaseName(requestPath)}.variables.json"
                    : null,
            };

            return file;
        }

        public IEnumerable<string> GetSidecarFiles()
        {
            if (!string.IsNullOrWhiteSpace(Body?.JsonFile))
                yield return Body.JsonFile;
            if (!string.IsNullOrWhiteSpace(QueryFile))
                yield return QueryFile;
            if (!string.IsNullOrWhiteSpace(VariablesFile))
                yield return VariablesFile;
        }

        private static List<KeyValuePair<string>> ParsePairs(object? value)
        {
            if (value is null)
            {
                return [];
            }

            var element = (JsonElement)value;
            if (element.ValueKind == JsonValueKind.Object)
            {
                return element.EnumerateObject()
                    .Select(property => new KeyValuePair<string> { Key = property.Name, Value = property.Value.GetString(), IsEnabled = true })
                    .ToList();
            }

            if (element.ValueKind == JsonValueKind.Array)
            {
                return element.Deserialize<List<KeyValuePair<string>>>(Options) ?? [];
            }

            return [];
        }
    }

    private sealed class LinkedRequestBodyFile
    {
        public JsonElement? Json { get; set; }
        public string? JsonFile { get; set; }
        public string? Xml { get; set; }
        public string? Text { get; set; }

        public static LinkedRequestBodyFile? FromRequest(HttpRequestEntry request, string requestPath)
        {
            if (request.Body.Mode == RequestBodyMode.None)
            {
                return null;
            }

            if (request.Body.Mode == RequestBodyMode.Json && !string.IsNullOrWhiteSpace(request.Body.RawContent))
            {
                return new LinkedRequestBodyFile { JsonFile = $"{GetRequestBaseName(requestPath)}.body.json" };
            }

            if (request.Body.Mode == RequestBodyMode.Xml && !string.IsNullOrWhiteSpace(request.Body.RawContent))
            {
                return new LinkedRequestBodyFile { Xml = request.Body.RawContent };
            }

            if (request.Body.Mode == RequestBodyMode.Text && !string.IsNullOrWhiteSpace(request.Body.RawContent))
            {
                return new LinkedRequestBodyFile { Text = request.Body.RawContent };
            }

            return null;
        }
    }

    private sealed class SwebKitEnvironmentFile
    {
        public string? Name { get; set; }
        public Dictionary<string, string?> Variables { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, VariableGeneratorDefinition> GeneratedVariables { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, LinkedSecretReference> Secrets { get; set; } = new(StringComparer.Ordinal);

        public ApiEnvironment ToEnvironment(string path)
        {
            var environment = new ApiEnvironment
            {
                Id = StableId(path),
                Name = string.IsNullOrWhiteSpace(Name) ? TitleFromFileName(Path.GetFileName(path)) : Name,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            foreach (var variable in Variables.Where(static pair => !string.IsNullOrWhiteSpace(pair.Key)))
            {
                environment.Variables.Add(new EnvironmentVariable
                {
                    Key = variable.Key,
                    Value = variable.Value,
                    IsEnabled = true,
                });
            }

            foreach (var secret in Secrets.Where(static pair => !string.IsNullOrWhiteSpace(pair.Key)))
            {
                var provider = secret.Value.Provider ?? "CredentialStore";
                environment.Variables.Add(new EnvironmentVariable
                {
                    Key = $"secret:{secret.Key}",
                    CredentialKey = secret.Value.Ref,
                    KeyVaultName = secret.Value.Vault,
                    SecretSource = provider.Equals("KeyVault", StringComparison.OrdinalIgnoreCase)
                        ? EnvironmentVariableSecretSource.AzureKeyVault
                        : EnvironmentVariableSecretSource.WindowsCredentialStore,
                    IsEnabled = true,
                });
            }

            foreach (var generated in GeneratedVariables.Where(static pair => !string.IsNullOrWhiteSpace(pair.Key)))
            {
                environment.Variables.Add(new EnvironmentVariable
                {
                    Key = generated.Key,
                    SecretSource = EnvironmentVariableSecretSource.Generated,
                    Generator = generated.Value,
                    IsEnabled = true,
                });
            }

            return environment;
        }

        public static SwebKitEnvironmentFile FromEnvironment(ApiEnvironment environment)
        {
            var file = new SwebKitEnvironmentFile { Name = environment.Name };

            foreach (var variable in environment.Variables.Where(static variable => variable.IsEnabled && !string.IsNullOrWhiteSpace(variable.Key)))
            {
                if (variable.Key.StartsWith("secret:", StringComparison.OrdinalIgnoreCase))
                {
                    var secretName = variable.Key["secret:".Length..];
                    file.Secrets[secretName] = new LinkedSecretReference
                    {
                        Provider = variable.SecretSource == EnvironmentVariableSecretSource.AzureKeyVault ? "KeyVault" : "CredentialStore",
                        Ref = variable.CredentialKey ?? string.Empty,
                        Vault = variable.KeyVaultName,
                    };
                }
                else if (variable.SecretSource == EnvironmentVariableSecretSource.Plain)
                {
                    file.Variables[variable.Key] = variable.Value;
                }
                else if (variable.SecretSource == EnvironmentVariableSecretSource.Generated && variable.Generator is not null)
                {
                    file.GeneratedVariables[variable.Key] = variable.Generator;
                }
            }

            return file;
        }
    }

    private sealed class LinkedSecretReference
    {
        public string? Provider { get; set; }
        public string Ref { get; set; } = string.Empty;
        public string? Vault { get; set; }
    }

    private static string GetRequestBaseName(string requestPath)
    {
        var fileName = Path.GetFileName(requestPath);
        return fileName.EndsWith(RequestFileExtension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^RequestFileExtension.Length]
            : Path.GetFileNameWithoutExtension(fileName);
    }
}
