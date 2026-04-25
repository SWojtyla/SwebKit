namespace SwebKit.Core.Configuration;

internal static class AppDataFileStore
{
    public static string GetBackupPath(string filePath) => $"{filePath}.bak";

    public static bool Exists(string filePath) =>
        File.Exists(filePath) || File.Exists(GetBackupPath(filePath));

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
                    Value: await ReadAndDeserializeAsync(filePath, deserialize),
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
                        Value: await ReadAndDeserializeAsync(backupPath, deserialize),
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
                Value: await ReadAndDeserializeAsync(backupPath, deserialize),
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
            await File.WriteAllTextAsync(tempPath, contents);
            CommitTempFile(tempPath, filePath);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        TryRefreshBackup(filePath);
    }

    private static async Task<T> ReadAndDeserializeAsync<T>(string filePath, Func<string, T> deserialize)
    {
        var content = await File.ReadAllTextAsync(filePath);
        return deserialize(content);
    }

    private static void TryRefreshBackup(string filePath)
    {
        var backupPath = GetBackupPath(filePath);
        var backupTempPath = $"{backupPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            File.Copy(filePath, backupTempPath);
            CommitTempFile(backupTempPath, backupPath);
        }
        catch
        {
            TryDelete(backupTempPath);
        }
    }

    private static void CommitTempFile(string tempPath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            File.Replace(tempPath, destinationPath, null, ignoreMetadataErrors: true);
            return;
        }

        try
        {
            File.Move(tempPath, destinationPath);
        }
        catch (IOException) when (File.Exists(destinationPath))
        {
            File.Replace(tempPath, destinationPath, null, ignoreMetadataErrors: true);
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