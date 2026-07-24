namespace SwebKit.Core.Configuration;

internal static class AppDataFileStore
{
    public static string GetBackupPath(string filePath) => $"{filePath}.bak";

    public static string GetUnreadableSnapshotPath(string filePath) => $"{filePath}.unreadable";

    public static bool Exists(string filePath) =>
        File.Exists(filePath) || File.Exists(GetBackupPath(filePath));

    /// <summary>
    /// Best-effort snapshot of a file (and its <c>.bak</c> sibling, if present) that failed to
    /// deserialize into the current shape. Callers must invoke this <em>before</em> resetting
    /// their in-memory store to defaults on a load failure — without it, the very next
    /// <see cref="SaveAsync"/> would overwrite the only surviving copy of the user's data with an
    /// empty store, turning a transient shape mismatch into permanent data loss. The snapshot uses
    /// a fixed name (not timestamped) so repeated failed launches don't pile up copies, and it is
    /// never itself consulted by <see cref="Exists"/>/<see cref="LoadAsync{T}"/>, so it can't
    /// interfere with normal load/save operation.
    /// </summary>
    /// <returns><see langword="true"/> if at least one snapshot was successfully created; <see langword="false"/> if every copy attempt failed.</returns>
    public static bool PreserveUnreadableFile(string filePath)
    {
        var primaryOk = TryCopyOver(filePath, GetUnreadableSnapshotPath(filePath));
        var backupOk = TryCopyOver(GetBackupPath(filePath), GetUnreadableSnapshotPath(GetBackupPath(filePath)));
        return primaryOk || backupOk;
    }

    private static bool TryCopyOver(string sourcePath, string destinationPath)
    {
        try
        {
            if (File.Exists(sourcePath))
            {
                File.Copy(sourcePath, destinationPath, overwrite: true);
                return true;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return false;
    }

    public static async Task<AppDataFileLoadResult<T>> LoadAsync<T>(
        string filePath,
        Func<string, T> deserialize)
    {
        var backupPath = GetBackupPath(filePath);

        if (File.Exists(filePath))
        {
            try
            {
                return new AppDataFileLoadResult<T>(
                    TargetPath: filePath,
                    SourcePath: filePath,
                    Value: await ReadAndDeserializeAsync(filePath, deserialize).ConfigureAwait(false),
                    WasRecovered: false,
                    PrimaryErrorMessage: null);
            }
            catch (Exception primaryEx)
            {
                if (!File.Exists(backupPath))
                {
                    throw;
                }

                try
                {
                    return new AppDataFileLoadResult<T>(
                        TargetPath: filePath,
                        SourcePath: backupPath,
                        Value: await ReadAndDeserializeAsync(backupPath, deserialize).ConfigureAwait(false),
                        WasRecovered: true,
                        PrimaryErrorMessage: primaryEx.Message);
                }
                catch (Exception backupEx)
                {
                    throw new InvalidOperationException(
                        $"Failed to load '{filePath}' and its backup '{backupPath}'. Primary error: {primaryEx.Message} Backup error: {backupEx.Message}",
                        backupEx);
                }
            }
        }

        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException($"No file or backup exists for '{filePath}'.", filePath);
        }

        try
        {
            return new AppDataFileLoadResult<T>(
                TargetPath: filePath,
                SourcePath: backupPath,
                Value: await ReadAndDeserializeAsync(backupPath, deserialize).ConfigureAwait(false),
                WasRecovered: true,
                PrimaryErrorMessage: null);
        }
        catch (Exception backupEx)
        {
            throw new InvalidOperationException(
                $"Failed to load backup '{backupPath}' while '{filePath}' was missing. {backupEx.Message}",
                backupEx);
        }
    }

    public static async Task SaveAsync(string filePath, string contents)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"Could not determine a directory for '{filePath}'.");
        }

        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(tempPath, contents).ConfigureAwait(false);
            await ReplaceOrMoveWithRetryAsync(tempPath, filePath).ConfigureAwait(false);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        TryRefreshBackup(filePath);
    }

    /// <summary>
    /// Swaps the freshly-written temp file into place, retrying briefly on a transient sharing
    /// violation. The target file can be transiently locked by another concurrent writer (e.g.
    /// antivirus/indexer scanning, or another save in flight for the same path) — Windows surfaces
    /// this as either <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>
    /// depending on the lock type. A short backoff-and-retry resolves this without surfacing a
    /// spurious failure to the caller.
    /// </summary>
    private static async Task ReplaceOrMoveWithRetryAsync(string tempPath, string filePath)
    {
        const int maxAttempts = 8;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Replace(tempPath, filePath, null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, filePath, overwrite: true);
                }

                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < maxAttempts)
            {
                await Task.Delay(25 * attempt).ConfigureAwait(false);
            }
        }
    }

    private static async Task<T> ReadAndDeserializeAsync<T>(string filePath, Func<string, T> deserialize)
    {
        var content = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        return deserialize(content);
    }

    private static void TryRefreshBackup(string filePath)
    {
        var backupPath = GetBackupPath(filePath);
        var backupTempPath = $"{backupPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            File.Copy(filePath, backupTempPath);

            if (File.Exists(backupPath))
            {
                File.Replace(backupTempPath, backupPath, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(backupTempPath, backupPath);
            }
        }
        catch
        {
            TryDelete(backupTempPath);
        }
    }

    private static void TryDelete(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
        }
    }
}

internal sealed record AppDataFileLoadResult<T>(
    string TargetPath,
    string SourcePath,
    T Value,
    bool WasRecovered,
    string? PrimaryErrorMessage);