using System.IO.Compression;
using System.Text;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Exports a collection to a ZIP archive containing one <c>.bru</c> file per request,
/// mirroring the Bruno collection format used on disk.
/// Folder hierarchy is represented as subdirectories inside the ZIP.
/// </summary>
public sealed class BrunoCollectionExporter : ICollectionExporter
{
    public string FileExtension => ".zip";
    public string FormatName => "Bruno (.bru)";

    public Task<byte[]> ExportAsync(
        ApiCollection collection,
        IReadOnlyList<ApiEnvironment> environments,
        CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var collectionFolder = Sanitize(collection.Name) + "/";

            // Write bruno.json manifest
            var manifest = $$"""
                {
                  "version": "1",
                  "name": "{{JsonEscape(collection.Name)}}",
                  "type": "collection"
                }
                """;
            WriteEntry(zip, collectionFolder + "bruno.json", manifest);

            // Write each request recursively
            WriteNodes(zip, collection.Nodes, collectionFolder);

            // Write environments as Bruno .bru environment files in an 'environments' folder
            foreach (var env in environments)
            {
                var envContent = BuildEnvFile(env);
                WriteEntry(zip, collectionFolder + "environments/" + Sanitize(env.Name) + ".bru", envContent);
            }
        }

        return Task.FromResult(ms.ToArray());
    }

    /// <summary>
    /// Exports a collection to a folder on disk, writing one <c>.bru</c> file per request,
    /// mirroring the Bruno collection format. Folder hierarchy is represented as subdirectories.
    /// Unlike <see cref="ExportAsync"/>, this writes directly to the filesystem instead of a ZIP.
    /// </summary>
    public Task ExportToFolderAsync(
        ApiCollection collection,
        IReadOnlyList<ApiEnvironment> environments,
        string targetFolderPath,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetFolderPath);

        // Write bruno.json manifest
        var manifest = $$"""
            {
              "version": "1",
              "name": "{{JsonEscape(collection.Name)}}",
              "type": "collection"
            }
            """;
        File.WriteAllText(Path.Combine(targetFolderPath, "bruno.json"), manifest);

        // Write each request recursively to disk
        WriteNodesToFolder(targetFolderPath, collection.Nodes);

        // Write environments
        var envFolder = Path.Combine(targetFolderPath, "environments");
        if (environments.Count > 0)
        {
            Directory.CreateDirectory(envFolder);
            foreach (var env in environments)
            {
                var envContent = BuildEnvFile(env);
                File.WriteAllText(Path.Combine(envFolder, Sanitize(env.Name) + ".bru"), envContent);
            }
        }

        return Task.CompletedTask;
    }

    private static void WriteNodesToFolder(string folderPath, List<ApiCollectionNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Type == ApiCollectionNodeType.Folder)
            {
                var subDir = Path.Combine(folderPath, Sanitize(node.Name));
                Directory.CreateDirectory(subDir);
                File.WriteAllText(Path.Combine(subDir, "meta.bru"), $"meta {{\n  name: {node.Name}\n  seq: 1\n}}\n");
                WriteNodesToFolder(subDir, node.Children);
            }
            else if (node.Type == ApiCollectionNodeType.Request && node.Request is not null)
            {
                var content = BuildBruFile(node.Request);
                File.WriteAllText(Path.Combine(folderPath, Sanitize(node.Request.Name) + ".bru"), content);
            }
        }
    }

    private static void WriteNodes(ZipArchive zip, List<ApiCollectionNode> nodes, string pathPrefix)
    {
        foreach (var node in nodes)
        {
            if (node.Type == ApiCollectionNodeType.Folder)
            {
                var subDir = pathPrefix + Sanitize(node.Name) + "/";
                // Write a folder marker (Bruno uses empty .gitkeep or a meta.bru)
                WriteEntry(zip, subDir + "meta.bru", $"meta {{\n  name: {node.Name}\n  seq: 1\n}}\n");
                WriteNodes(zip, node.Children, subDir);
            }
            else if (node.Type == ApiCollectionNodeType.Request && node.Request is not null)
            {
                var content = BuildBruFile(node.Request);
                WriteEntry(zip, pathPrefix + Sanitize(node.Request.Name) + ".bru", content);
            }
        }
    }

    internal static string BuildBruFile(HttpRequestEntry req)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"meta {{");
        sb.AppendLine($"  name: {req.Name}");
        sb.AppendLine($"  type: http");
        sb.AppendLine($"  seq: 1");
        sb.AppendLine($"}}");
        sb.AppendLine();

        var method = MapMethod(req.Method);
        sb.AppendLine($"{method} {{");
        sb.AppendLine($"  url: {req.Url}");
        sb.AppendLine($"  body: {MapBodyMode(req.Body.Mode)}");
        sb.AppendLine($"}}");

        if (req.Headers.Any(h => h.IsEnabled))
        {
            sb.AppendLine();
            sb.AppendLine("headers {");
            foreach (var h in req.Headers.Where(h => h.IsEnabled && !string.IsNullOrWhiteSpace(h.Key)))
                sb.AppendLine($"  {h.Key}: {h.Value}");
            sb.AppendLine("}");
        }

        if (req.QueryParams.Any(p => p.IsEnabled))
        {
            sb.AppendLine();
            sb.AppendLine("query {");
            foreach (var p in req.QueryParams.Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Key)))
                sb.AppendLine($"  {p.Key}: {p.Value}");
            sb.AppendLine("}");
        }

        if (req.Body.Mode is RequestBodyMode.Json or RequestBodyMode.Xml or RequestBodyMode.Text &&
            !string.IsNullOrWhiteSpace(req.Body.RawContent))
        {
            var tag = req.Body.Mode == RequestBodyMode.Json ? "body:json" : "body:text";
            sb.AppendLine();
            sb.AppendLine($"{tag} {{");
            sb.AppendLine(req.Body.RawContent);
            sb.AppendLine("}");
        }

        if (req.Method == ApiRequestMethod.GraphQl)
        {
            sb.AppendLine();
            sb.AppendLine("body:graphql {");
            if (!string.IsNullOrWhiteSpace(req.GraphQlQuery))
                sb.AppendLine(req.GraphQlQuery);
            sb.AppendLine("}");

            if (!string.IsNullOrWhiteSpace(req.GraphQlVariables))
            {
                sb.AppendLine();
                sb.AppendLine("body:graphql:vars {");
                sb.AppendLine(req.GraphQlVariables);
                sb.AppendLine("}");
            }
        }

        return sb.ToString();
    }

    private static string BuildEnvFile(ApiEnvironment env)
    {
        var sb = new StringBuilder();
        sb.AppendLine("vars {");
        foreach (var v in env.Variables.Where(v => v.IsEnabled && !string.IsNullOrWhiteSpace(v.Key)))
        {
            var value = v.SecretSource == EnvironmentVariableSecretSource.Plain
                ? (v.Value ?? "")
                : ""; // Secrets not exported in plaintext
            sb.AppendLine($"  {v.Key}: {value}");
        }
        sb.AppendLine("}");

        var secretVars = env.Variables.Where(v => v.IsEnabled && v.SecretSource != EnvironmentVariableSecretSource.Plain).ToList();
        if (secretVars.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("vars:secret [");
            foreach (var v in secretVars)
                sb.AppendLine($"  {v.Key},");
            sb.AppendLine("]");
        }

        return sb.ToString();
    }

    private static void WriteEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static string MapMethod(ApiRequestMethod method) => method switch
    {
        ApiRequestMethod.Post => "post",
        ApiRequestMethod.Put => "put",
        ApiRequestMethod.Patch => "patch",
        ApiRequestMethod.Delete => "delete",
        ApiRequestMethod.Head => "head",
        ApiRequestMethod.Options => "options",
        ApiRequestMethod.GraphQl => "post",
        _ => "get",
    };

    private static string MapBodyMode(RequestBodyMode mode) => mode switch
    {
        RequestBodyMode.Json => "json",
        RequestBodyMode.Xml => "xml",
        RequestBodyMode.Text => "text",
        RequestBodyMode.FormData => "multipartForm",
        RequestBodyMode.Binary => "file",
        _ => "none",
    };

    /// <summary>Strips characters that are invalid in file/directory names.</summary>
    private static string Sanitize(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private static string JsonEscape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
