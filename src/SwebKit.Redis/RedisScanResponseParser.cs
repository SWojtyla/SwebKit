using StackExchange.Redis;

namespace SwebKit.Redis;

internal static class RedisScanResponseParser
{
    public static RedisScanPage Parse(RedisResult result)
    {
        if (result.IsNull)
        {
            return new RedisScanPage(0, [], true);
        }

        var parts = (RedisResult[])result!;
        if (parts.Length < 2)
        {
            return new RedisScanPage(0, [], true);
        }

        var nextCursor = ParseCursor(parts[0].ToString());
        var values = ((RedisResult[])parts[1]!)
            .Select(value => value.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();

        return new RedisScanPage(nextCursor, values, nextCursor == 0);
    }

    private static long ParseCursor(string? rawCursor) => long.TryParse(rawCursor, out var cursor)
        ? cursor
        : 0;
}

internal sealed record RedisScanPage(long Cursor, IReadOnlyList<string> Values, bool IsComplete);