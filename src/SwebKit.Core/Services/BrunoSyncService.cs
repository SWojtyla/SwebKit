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
    public async Task<string?> SyncRequestSaveAsync(
        string brunoFolderPath,
        ApiCollection collection,
        HttpRequestEntry request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(brunoFolderPath))
                return $"Bruno sync folder not found: {brunoFolderPath}";

            cancellationToken.ThrowIfCancellationRequested();

            var bruContent = BrunoCollectionExporter.BuildBruFile(request);

            // Prefer the request's own folder; only fall back to a tree-wide match when the
            // resolved folder has no file (folder names don't always map 1:1 to on-disk dirs).
            var existingFile = await FindExistingBruFileAsync(
                brunoFolderPath, collection, request, request.Name, cancellationToken).ConfigureAwait(false);
            if (existingFile is not null)
            {
                await File.WriteAllTextAsync(existingFile, bruContent, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Create a new .bru file in the expected location based on the collection tree.
                var expectedFolderPath = ResolveBrunoFolderPath(brunoFolderPath, collection, request.Id);
                Directory.CreateDirectory(expectedFolderPath);
                var filePath = GetUniqueBruFilePath(expectedFolderPath, request.Name);
                await File.WriteAllTextAsync(filePath, bruContent, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Deletes the .bru file for the given request from the Bruno folder. The request must still
    /// be present in <paramref name="collection"/>'s node tree so its folder can be resolved.
    /// Returns <c>null</c> on success (or if the file was not found), an error message on failure.
    /// </summary>
    public async Task<string?> SyncRequestDeleteAsync(
        string brunoFolderPath,
        ApiCollection collection,
        HttpRequestEntry request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(brunoFolderPath))
                return $"Bruno sync folder not found: {brunoFolderPath}";

            cancellationToken.ThrowIfCancellationRequested();

            var existingFile = await FindExistingBruFileAsync(
                brunoFolderPath, collection, request, request.Name, cancellationToken).ConfigureAwait(false);
            if (existingFile is not null)
            {
                File.Delete(existingFile);
            }

            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Renames the .bru file for the given request in the Bruno folder.
    /// Updates the meta name inside the file and renames the file on disk.
    /// Returns <c>null</c> on success, an error message on failure.
    /// </summary>
    public async Task<string?> SyncRequestRenameAsync(
        string brunoFolderPath,
        ApiCollection collection,
        HttpRequestEntry request,
        string oldName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(brunoFolderPath))
                return $"Bruno sync folder not found: {brunoFolderPath}";

            cancellationToken.ThrowIfCancellationRequested();

            // Match on the OLD name (the file on disk still carries it), scoped to the request's folder.
            var existingFile = await FindExistingBruFileAsync(
                brunoFolderPath, collection, request, oldName, cancellationToken).ConfigureAwait(false);
            var bruContent = BrunoCollectionExporter.BuildBruFile(request);

            if (existingFile is not null)
            {
                // Rewrite content with the new name.
                await File.WriteAllTextAsync(existingFile, bruContent, cancellationToken).ConfigureAwait(false);

                // Rename the file to match the new name.
                var dir = Path.GetDirectoryName(existingFile)!;
                var newFilePath = GetUniqueBruFilePath(dir, request.Name);
                if (!string.Equals(existingFile, newFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Move(existingFile, newFilePath, overwrite: true);
                }
            }
            else
            {
                // File not found by old name — create it with the new name.
                var expectedFolderPath = ResolveBrunoFolderPath(brunoFolderPath, collection, request.Id);
                Directory.CreateDirectory(expectedFolderPath);
                var filePath = GetUniqueBruFilePath(expectedFolderPath, request.Name);
                await File.WriteAllTextAsync(filePath, bruContent, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Locates the on-disk .bru file for a request. Prefers a match inside the request's resolved
    /// folder — this disambiguates requests that share a name across different folders, so a
    /// save/rename/delete never touches a same-named request elsewhere in the tree. Falls back to
    /// a tree-wide search only when the resolved folder holds no match, because a folder node's
    /// name can diverge from its on-disk directory name (Bruno <c>folder.bru</c> display names).
    /// </summary>
    private static async Task<string?> FindExistingBruFileAsync(
        string brunoFolderPath,
        ApiCollection collection,
        HttpRequestEntry request,
        string matchName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(matchName))
            return null;

        var resolvedFolder = ResolveBrunoFolderPath(brunoFolderPath, collection, request.Id);
        var scoped = await FindBruFileByMetaNameInDirectoryAsync(resolvedFolder, matchName, cancellationToken)
            .ConfigureAwait(false);
        if (scoped is not null)
            return scoped;

        return await FindBruFileByMetaNameAsync(brunoFolderPath, matchName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Searches only the direct .bru files of <paramref name="directory"/> (non-recursive) for one
    /// whose <c>meta { name: }</c> matches <paramref name="requestName"/>.
    /// </summary>
    private static async Task<string?> FindBruFileByMetaNameInDirectoryAsync(
        string directory, string requestName, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
            return null;

        string[] files;
        try { files = Directory.GetFiles(directory, "*.bru"); }
        catch (UnauthorizedAccessException) { return null; }
        catch (DirectoryNotFoundException) { return null; }

        foreach (var file in files)
        {
            if (IsStructuralBruFile(Path.GetFileName(file)))
                continue;
            var metaName = await ExtractMetaNameAsync(file, cancellationToken).ConfigureAwait(false);
            if (string.Equals(metaName, requestName, StringComparison.Ordinal))
                return file;
        }

        return null;
    }

    /// <summary>
    /// Searches the whole Bruno folder tree for the first .bru file whose <c>meta { name: }</c>
    /// matches <paramref name="requestName"/>. Skips meta/collection/folder .bru files and ignored dirs.
    /// </summary>
    private static async Task<string?> FindBruFileByMetaNameAsync(
        string brunoFolderPath, string requestName, CancellationToken cancellationToken)
    {
        foreach (var file in EnumerateBruFiles(brunoFolderPath))
        {
            var metaName = await ExtractMetaNameAsync(file, cancellationToken).ConfigureAwait(false);
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
            if (IsStructuralBruFile(Path.GetFileName(file)))
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
    /// True for Bruno structural files (folder/collection metadata) that never represent a request.
    /// </summary>
    private static bool IsStructuralBruFile(string fileName) =>
        string.Equals(fileName, "meta.bru", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, "collection.bru", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, "folder.bru", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Extracts the <c>name</c> value from the <c>meta { }</c> block of a .bru file. Reads only far
    /// enough to leave the meta block, so a large request body never gets loaded into memory.
    /// </summary>
    private static async Task<string?> ExtractMetaNameAsync(string bruFilePath, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(bruFilePath);
            var inMeta = false;
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                var trimmed = line.Trim();
                if (!inMeta)
                {
                    if (trimmed.StartsWith("meta {", StringComparison.OrdinalIgnoreCase) || trimmed == "meta {")
                        inMeta = true;
                    continue;
                }

                if (trimmed.StartsWith('}'))
                    break;
                if (trimmed.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                    return trimmed["name:".Length..].Trim();
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* unreadable file — treat as no match */ }
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

    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    private static string Sanitize(string name) =>
        string.Concat(name.Select(c => InvalidFileNameChars.Contains(c) ? '_' : c));
}
