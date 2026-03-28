namespace SwebKit.Core.Models;

public enum PodHealthEventType
{
    PodFailed,          // Phase → Failed
    PodCrashLoop,       // Restart count jumped or container in CrashLoopBackOff
    PodUnknown,         // Phase → Unknown
    ContainerNotReady,  // Ready containers < total (was previously fully ready)
    PodTerminated       // Pod disappeared from namespace
}

public sealed record PodHealthEvent(
    string PodName,
    string Namespace,
    string ClusterContext,
    PodHealthEventType EventType,
    string PreviousPhase,
    string CurrentPhase,
    int RestartCount,
    DateTimeOffset DetectedAt,
    string? Message = null);
