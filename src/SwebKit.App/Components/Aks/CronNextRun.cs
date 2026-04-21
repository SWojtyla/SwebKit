using System;

namespace SwebKit.App.Components.Aks;

/// <summary>
/// Lightweight next-run calculator for standard 5-field Unix/Quartz cron expressions.
/// Does not support 6-field, @reboot, @yearly, @annually, @monthly, @weekly, @daily, @hourly — falls back gracefully.
/// </summary>
internal static class CronNextRun
{
    /// <summary>
    /// Attempts to calculate the next run time after <paramref name="from"/> for a 5-field cron expression.
    /// Returns false for unsupported or invalid expressions.
    /// </summary>
    public static bool TryCalculate(string? schedule, DateTimeOffset from, out DateTimeOffset next)
    {
        next = default;
        if (string.IsNullOrWhiteSpace(schedule)) return false;
        if (schedule.StartsWith('@')) return false;  // @daily, @reboot, etc.

        var parts = schedule.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5) return false;

        try
        {
            var candidate = from.ToUniversalTime().AddMinutes(1);
            // Cap search at 400 days (prevents infinite loop on malformed expressions)
            var limit = candidate.AddDays(400);

            while (candidate < limit)
            {
                if (!MatchesCronField(parts[3], candidate.Month, 1, 12))
                { candidate = new DateTimeOffset(candidate.Year, candidate.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1); continue; }

                if (!MatchesCronField(parts[4], (int)candidate.DayOfWeek, 0, 7))
                { candidate = TruncateToDay(candidate).AddDays(1); continue; }

                if (!MatchesCronField(parts[2], candidate.Day, 1, 31))
                { candidate = TruncateToDay(candidate).AddDays(1); continue; }

                if (!MatchesCronField(parts[1], candidate.Hour, 0, 23))
                { candidate = TruncateToHour(candidate).AddHours(1); continue; }

                if (!MatchesCronField(parts[0], candidate.Minute, 0, 59))
                { candidate = candidate.AddMinutes(1); continue; }

                next = candidate;
                return true;
            }
        }
        catch
        {
            // Malformed expression — fall back
        }
        return false;
    }

    private static DateTimeOffset TruncateToHour(DateTimeOffset dt) =>
        new(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset TruncateToDay(DateTimeOffset dt) =>
        new(dt.Year, dt.Month, dt.Day, 0, 0, 0, TimeSpan.Zero);

    private static bool MatchesCronField(string field, int value, int min, int max)
    {
        if (field == "*") return true;

        foreach (var part in field.Split(','))
        {
            if (part.Contains('/'))
            {
                var slashParts = part.Split('/');
                if (slashParts.Length != 2) return false;
                if (!int.TryParse(slashParts[1], out int step) || step <= 0) return false;
                int start = slashParts[0] == "*" ? min : int.Parse(slashParts[0]);
                for (int v = start; v <= max; v += step)
                    if (v == value) return true;
            }
            else if (part.Contains('-'))
            {
                var rangeParts = part.Split('-');
                if (rangeParts.Length != 2) return false;
                if (!int.TryParse(rangeParts[0], out var lo) || !int.TryParse(rangeParts[1], out var hi)) return false;
                // Day-of-week: 7 == Sunday == 0
                if (value == 0 && max == 7 && hi == 7) return true;
                if (lo <= value && value <= hi) return true;
            }
            else
            {
                if (!int.TryParse(part, out var v)) return false;
                // Day-of-week: 7 maps to Sunday (0)
                if (max == 7 && v == 7 && value == 0) return true;
                if (v == value) return true;
            }
        }
        return false;
    }

    public static string FormatCountdown(DateTimeOffset next, DateTimeOffset from)
    {
        var span = next - from;
        if (span.TotalDays >= 1)
            return $"in {(int)span.TotalDays}d {span.Hours}h (UTC)";
        if (span.TotalHours >= 1)
            return $"in {(int)span.TotalHours}h {span.Minutes}m (UTC)";
        return $"in {(int)span.TotalMinutes}m (UTC)";
    }
}
