using SwebKit.Core.Models;
using SwebKit.Observability;

namespace SwebKit.Core.Tests;

public sealed class LogQueryResultProjectorTests
{
    [Fact]
    public void Project_StopsAfterMaxRowsPlusOne_WhenDetectingTruncation()
    {
        var enumeratedRows = 0;
        var rows = EnumerateRows(20, () => enumeratedRows++);

        var result = LogQueryResultProjector.Project(
            columns: ["timestamp", "message"],
            rows,
            static (row, index) => row[index],
            executionTime: TimeSpan.FromMilliseconds(12),
            maxRows: 5);

        Assert.Equal(5, result.Rows.Count);
        Assert.True(result.Truncated);
        Assert.Equal(6, enumeratedRows);
    }

    [Fact]
    public void Project_WhenUnderLimit_ReturnsAllRowsWithoutTruncation()
    {
        var enumeratedRows = 0;
        var rows = EnumerateRows(3, () => enumeratedRows++);

        var result = LogQueryResultProjector.Project(
            columns: ["timestamp", "message"],
            rows,
            static (row, index) => row[index],
            executionTime: TimeSpan.FromMilliseconds(8),
            maxRows: 5);

        Assert.Equal(3, result.Rows.Count);
        Assert.False(result.Truncated);
        Assert.Equal(3, enumeratedRows);
    }

    private static IEnumerable<object?[]> EnumerateRows(int count, Action onYield)
    {
        for (var i = 0; i < count; i++)
        {
            onYield();
            yield return [DateTimeOffset.UtcNow.AddMinutes(i), $"row-{i}"];
        }
    }
}