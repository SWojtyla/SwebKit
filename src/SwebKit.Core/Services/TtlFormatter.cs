namespace SwebKit.Core.Services;

public enum TtlVisualState
{
    Success,
    Warning,
    Critical,
}

/// <summary>
/// Formats Redis TTL (TimeSpan?) values for human-readable display and progress-bar visualisation.
/// </summary>
public static class TtlFormatter
{
    /// <summary>
    /// Formats a TTL value as a human-readable string.
    /// <list type="bullet">
    /// <item>null  — "No expiry"</item>
    /// <item>≤ zero — "Key has no TTL / already expired"</item>
    /// <item>≥ 1 hour — "Xh Ym remaining"</item>
    /// <item>1–60 minutes — "Xm Ys remaining"</item>
    /// <item>&lt; 60 seconds — "Xs remaining"</item>
    /// </list>
    /// </summary>
    public static string FormatHuman(TimeSpan? ttl)
    {
        if (!ttl.HasValue)
            return "No expiry";

        if (ttl.Value <= TimeSpan.Zero)
            return "Key has no TTL / already expired";

        var t = ttl.Value;

        if (t.TotalHours >= 1)
        {
            var h = (int)t.TotalHours;
            return $"{h}h {t.Minutes}m remaining";
        }

        if (t.TotalMinutes >= 1)
        {
            var m = (int)t.TotalMinutes;
            return $"{m}m {t.Seconds}s remaining";
        }

        return $"{(int)t.TotalSeconds}s remaining";
    }

    /// <summary>
    /// Returns the severity bucket to use for TTL visualisation.
    /// When the original TTL is known, uses percentage thresholds; otherwise uses absolute thresholds.
    /// </summary>
    public static TtlVisualState GetVisualState(TimeSpan? remaining, TimeSpan? original)
    {
        if (!remaining.HasValue || remaining.Value <= TimeSpan.Zero)
            return TtlVisualState.Critical;

        if (original.HasValue && original.Value > TimeSpan.Zero)
        {
            var pct = remaining.Value.TotalSeconds / original.Value.TotalSeconds;
            return pct > 0.20 ? TtlVisualState.Success
                 : pct > 0.05 ? TtlVisualState.Warning
                 : TtlVisualState.Critical;
        }

        return remaining.Value.TotalMinutes > 5 ? TtlVisualState.Success
             : remaining.Value.TotalMinutes > 1 ? TtlVisualState.Warning
             : TtlVisualState.Critical;
    }

    /// <summary>
    /// Returns the CSS color variable to use for a TTL value.
    /// When the original TTL is known, uses percentage thresholds; otherwise uses absolute thresholds.
    /// </summary>
    public static string GetColor(TimeSpan? remaining, TimeSpan? original)
    {
        return GetVisualState(remaining, original) switch
        {
            TtlVisualState.Success => "var(--color-success)",
            TtlVisualState.Warning => "var(--color-warning)",
            _ => "var(--color-error)",
        };
    }

    /// <summary>
    /// Returns the width percentage (0–100) for the TTL expiry progress bar.
    /// When original TTL is known, uses percentage-based width;
    /// otherwise uses absolute seconds capped at 1 hour.
    /// </summary>
    public static double GetBarWidthPercent(TimeSpan? remaining, TimeSpan? original)
    {
        if (!remaining.HasValue || remaining.Value <= TimeSpan.Zero)
            return 0;

        if (original.HasValue && original.Value > TimeSpan.Zero)
        {
            var pct = remaining.Value.TotalSeconds / original.Value.TotalSeconds;
            return Math.Clamp(pct * 100, 0, 100);
        }

        // Absolute-based, capped at 1 hour (3 600 s)
        var fraction = Math.Min(remaining.Value.TotalSeconds / 3600.0, 1.0);
        return fraction * 100;
    }
}
