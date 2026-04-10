namespace SwebKit.Core.Services;

public sealed class RedisScanPageAccumulator
{
    private readonly int _pageSize;
    private readonly Queue<string> _overflowKeys = new();
    private readonly HashSet<string> _seenKeys = new(StringComparer.Ordinal);

    public RedisScanPageAccumulator(int pageSize)
    {
        _pageSize = Math.Max(1, pageSize);
    }

    public bool HasOverflow => _overflowKeys.Count > 0;

    public int OverflowCount => _overflowKeys.Count;

    public void Reset()
    {
        _overflowKeys.Clear();
        _seenKeys.Clear();
    }

    public void RegisterVisibleKey(string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
            _seenKeys.Add(key);
    }

    public IReadOnlyList<string> TakeOverflowPage(int currentPageCount)
    {
        var remainingCapacity = GetRemainingCapacity(currentPageCount);
        if (remainingCapacity == 0 || _overflowKeys.Count == 0)
            return [];

        var visibleKeys = new List<string>(Math.Min(remainingCapacity, _overflowKeys.Count));
        while (remainingCapacity > 0 && _overflowKeys.Count > 0)
        {
            visibleKeys.Add(_overflowKeys.Dequeue());
            remainingCapacity--;
        }

        return visibleKeys;
    }

    public RedisScanPageAppendResult AppendBatch(IReadOnlyList<string> keys, int currentPageCount)
    {
        var remainingCapacity = GetRemainingCapacity(currentPageCount);
        if (keys.Count == 0)
            return new([], _overflowKeys.Count, remainingCapacity == 0);

        var visibleKeys = new List<string>(Math.Min(remainingCapacity, keys.Count));
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key) || !_seenKeys.Add(key))
                continue;

            if (remainingCapacity > 0)
            {
                visibleKeys.Add(key);
                remainingCapacity--;
            }
            else
            {
                _overflowKeys.Enqueue(key);
            }
        }

        return new(visibleKeys, _overflowKeys.Count, remainingCapacity == 0);
    }

    private int GetRemainingCapacity(int currentPageCount) => Math.Max(0, _pageSize - Math.Max(0, currentPageCount));
}

public readonly record struct RedisScanPageAppendResult(IReadOnlyList<string> VisibleKeys, int OverflowCount, bool IsPageFull);