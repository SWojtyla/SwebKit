namespace SwebKit.Core.Constants;

/// <summary>Application-wide numeric limits and constants.</summary>
public static class Limits
{
    /// <summary>Maximum number of lines kept in the pod log view buffer.</summary>
    public const int LogBufferMaxLines = 50_000;

    /// <summary>Initial number of tail lines fetched when opening the All-history log view.</summary>
    public const int LogTailInitialLines = 2_000;

    /// <summary>Number of lines rendered in one visible log window.</summary>
    public const int LogVisibleWindowLines = 2_000;

    /// <summary>Number of additional history lines requested when the user loads older logs.</summary>
    public const int LogHistoryExpansionLines = 5_000;

    /// <summary>Upper bound for on-demand log history buffering in the UI.</summary>
    public const int LogHistoryHardCapLines = 200_000;

    /// <summary>Batch interval in ms for log viewer render updates.</summary>
    public const int LogRenderBatchIntervalMs = 150;

    /// <summary>Maximum bytes fetched for blob storage preview (512 KB).</summary>
    public const int StoragePreviewBytes = 524_288;

    /// <summary>Delay in ms before removing completed background tasks from the queue.</summary>
    public const int TaskCompletionDelayMs = 5_000;
}
