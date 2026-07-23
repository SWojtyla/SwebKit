using System.IO;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Handles best-effort .bru file write-back for linked collections that were imported from a
/// Bruno folder. Each method returns <c>null</c> on success or an error message on failure —
/// the caller should surface it as a non-blocking warning, never as a hard save failure.
/// </summary>
public sealed class BrunoSyncService
{
    private static readonly HashSet<string> IgnoredDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase) { "node_modules", ".git", ".svn", "environments" };

    /// <summary>
    /// Writes (or creates) the .bru file for the given request in the Bruno folder.
    /// Returns <c>null</c> on success, an error message on failure.
    /// </summary>
    public Task<string?> SyncRequestSaveAsync(
        string brunoFolderPath,
        ApiCollection collection,
        HttpRequestEntry request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(brunoFolderPath))
                return Task.FromResult<string?>($"Bruno sync folder not found: {brunoFolderPath}");

            cancellationToken.ThrowIfCancellationRequested();

            var bruContent = BrunoCollectionExporter.BuildBruFile(request);

            // Try to find an existing .bru file for this request (by meta name)
            var existingFile = FindBruFileByMetaName(brunoFolderPath, request.Name);
            if (existingFile is not null)
            {
                File.WriteAllText(existingFile, bruContent);
            }
            else
            {
                // Create a new .bru file in the expected location based on the collection tree
                var expectedFolderPath = ResolveBrunoFolderPath(brunoFolderPath, collection, request.Id);
                Directory.CreateDirectory(expectedFolderPath);
                var filePath = GetUniqueBruFilePath(expectedFolderPath, request.Name);
                File.WriteAllText(filePath, bruContent);
            }

            return Task.FromResult<string?>(null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return Task.FromResult<string?>(ex.Message);
        }
    }

    /// <summary>
    /// Deletes the .bru file for the given request from the Bruno folder.
    /// Returns <c>null</c> on success (or if the file was not found), an error message on failure.
    /// </summary>
    public Task<string?> SyncRequestDeleteAsync(
        string brunoFolderPath,
        string requestName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(brunoFolderPath))
                return Task.FromResult<string?>($"Bruno sync folder not found: {brunoFolderPath}");

            cancellationToken.ThrowIfCancellationRequested();

            var existingFile = FindBruFileByMetaName(brunoFolderPath, requestName);
            if (existingFile is not null)
            {
                File.Delete(existingFile);
            }

            return Task.FromResult<string?>(null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return Task.FromResult<string?>(ex.Message);
        }
    }

    /// <summary>
    /// Renames the .bru file for the given request in the Bruno folder.
    /// Updates the meta name inside the file and renames the file on disk.
    /// Returns <c>null</c> on success, an error message on failure.
    /// </summary>
    public Task<string?> SyncRequestRenameAsync(
        string brunoFolderPath,
        ApiCollection collection,
        HttpRequestEntry request,
        string oldName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(brunoFolderPath))
                return Task.FromResult<string?>($"Bruno sync folder not found: {brunoFolderPath}");

            cancellationToken.ThrowIfCancellationRequested();

            var existingFile = FindBruFileByMetaName(brunoFolderPath, oldName);
            if (existingFile is not null)
            {
                // Rewrite content with the new name
                var bruContent = BrunoCollectionExporter.BuildBruFile(request);
                File.WriteAllText(existingFile, bruContent);

                // Rename the file to match the new name
                var dir = Path.GetDirectoryName(existingFile)!;
                var newFilePath = GetUniqueBruFilePath(dir, request.Name);
                if (!string.Equals(existingFile, newFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Move(existingFile, newFilePath, overwrite: true);
                }
            }
            else
            {
                // File not found by old name — create it with the new name
                var expectedFolderPath = ResolveBrunoFolderPath(brunoFolderPath, collection, request.Id);
                Directory.CreateDirectory(expectedFolderPath);
                var bruContent = BrunoCollectionExporter.BuildBruFile(request);
                var filePath = GetUniqueBruFilePath(expectedFolderPath, request.Name);
                File.WriteAllText(filePath, bruContent);
            }

            return Task.FromResult<string?>(null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return Task.FromResult<string?>(ex.Message);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Searches the Bruno folder tree for a .bru file whose <c>meta { name: }</c> matches
    /// <paramref name="requestName"/>. Skips meta/collection/folder .bru files and ignored dirs.
    /// </summary>
    private static string? FindBruFileByMetaName(string brunoFolderPath, string requestName)
    {
        if (string.IsNullOrWhiteSpace(requestName))
            return null;

        foreach (var file in EnumerateBruFiles(brunoFolderPath))
        {
            var metaName = ExtractMetaName(file);
            if (string.Equals(metaName, requestName, StringComparison.Ordinal))
                return file;
        }

        return null;
    }

    /// <summary>
    /// Recursively enumerates .bru files, skipping ignored directories and meta/collection/folder files.
    /// </summary>
    private static IEnumerable<string> EnumerateBruFiles(string directory)
    {
        string[] files;
        try { files = Directory.GetFiles(directory, "*.bru"); }
        catch (UnauthorizedAccessException) { yield break; }
        catch (DirectoryNotFoundException) { yield break; }

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            if (string.Equals(fileName, "meta.bru", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "collection.bru", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "folder.bru", StringComparison.OrdinalIgnoreCase))
                continue;
            yield return file;
        }

        string[] subDirs;
        try { subDirs = Directory.GetDirectories(directory); }
        catch (UnauthorizedAccessException) { yield break; }
        catch (DirectoryNotFoundException) { yield break; }

        foreach (var subDir in subDirs)
        {
            var dirName = Path.GetFileName(subDir);
            if (IgnoredDirectoryNames.Contains(dirName)) continue;
            foreach (var file in EnumerateBruFiles(subDir))
                yield return file;
        }
    }

    /// <summary>
    /// Extracts the <c>name</c> value from the <c>meta { }</c> block of a .bru file.
    /// </summary>
    private static string? ExtractMetaName(string bruFilePath)
    {
        try
        {
            var content = File.ReadAllText(bruFilePath);
            var lines = content.ReplaceLineEndings("\n").Split('\n');
            var inMeta = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("meta {", StringComparison.OrdinalIgnoreCase) ||
                    trimmed == "meta {")
                {
                    inMeta = true;
                    continue;
                }
                if (inMeta)
                {
                    if (trimmed.StartsWith('}'))
                        break;
                    if (trimmed.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                        return trimmed["name:".Length..].Trim();
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Walks the collection's node tree to find the parent folder path of the request
    /// with the given ID, and maps it to the corresponding path in the Bruno folder.
    /// </summary>
    private static string ResolveBrunoFolderPath(string brunoFolderPath, ApiCollection collection, string requestId)
    {
        var folderPath = new List<string>();
        FindParentFolderPath(collection.Nodes, requestId, folderPath);

        var result = brunoFolderPath;
        foreach (var folder in folderPath)
            result = Path.Combine(result, Sanitize(folder));
        return result;
    }

    private static bool FindParentFolderPath(List<ApiCollectionNode> nodes, string requestId, List<string> path)
    {
        foreach (var node in nodes)
        {
            if (node.Type == ApiCollectionNodeType.Request && node.Request?.Id == requestId)
                return true;
        }

        foreach (var node in nodes)
        {
            if (node.Type == ApiCollectionNodeType.Folder)
            {
                path.Add(node.Name);
                if (FindParentFolderPath(node.Children, requestId, path))
                    return true;
                path.RemoveAt(path.Count - 1);
            }
        }

        return false;
    }

    private static string GetUniqueBruFilePath(string directory, string requestName)
    {
        var slug = Sanitize(requestName);
        var candidate = Path.Combine(directory, $"{slug}.bru");
        if (!File.Exists(candidate))
            return candidate;

        for (var index = 2; ; index++)
        {
            candidate = Path.Combine(directory, $"{slug}-{index}.bru");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    private static string Sanitize(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
