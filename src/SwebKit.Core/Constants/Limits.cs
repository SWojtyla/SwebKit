namespace SwebKit.Core.Constants;

/// <summary>Application-wide numeric limits and constants.</summary>
public static class Limits
{
    /// <summary>Maximum number of lines kept in the pod log view buffer.</summary>
    public const int LogBufferMaxLines = 10_000;

    /// <summary>Initial number of tail lines fetched when opening a pod log view.</summary>
    public const int LogTailInitialLines = 500;

    /// <summary>Maximum bytes fetched for blob storage preview (512 KB).</summary>
    public const int StoragePreviewBytes = 524_288;

    /// <summary>Delay in ms before removing completed background tasks from the queue.</summary>
    public const int TaskCompletionDelayMs = 5_000;
}
