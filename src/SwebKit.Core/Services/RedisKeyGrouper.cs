using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

/// <summary>
/// Groups Redis keys into a namespace tree and computes prefix memory distribution.
/// All methods are deterministic and operate on in-memory collections.
/// </summary>
public static class RedisKeyGrouper
{
    /// <summary>
    /// Builds a namespace tree from a flat list of keys using the given separator.
    /// </summary>
    public static List<NamespaceNode> BuildNamespaceTree(IEnumerable<string> keys, string separator)
    {
        if (string.IsNullOrEmpty(separator))
            return [];

        var root = new NamespaceNode { Name = "", FullPrefix = "" };

        foreach (var key in keys)
        {
            var parts = key.Split(separator, StringSplitOptions.None);
            Insert(root, parts, separator);
        }

        return root.Children;
    }

    private static void Insert(NamespaceNode parent, string[] parts, string separator)
    {
        var current = parent;

        for (var i = 0; i < parts.Length; i++)
        {
            var segment = parts[i];
            var child = current.Children.FirstOrDefault(c => c.Name == segment);

            if (child is null)
            {
                var prefix = current.FullPrefix.Length == 0
                    ? segment
                    : $"{current.FullPrefix}{separator}{segment}";

                child = new NamespaceNode { Name = segment, FullPrefix = prefix };

                // Insert sorted
                var idx = current.Children.FindIndex(c =>
                    string.Compare(c.Name, segment, StringComparison.Ordinal) > 0);
                if (idx < 0)
                    current.Children.Add(child);
                else
                    current.Children.Insert(idx, child);
            }

            child.KeyCount++;
            current = child;
        }
    }

    /// <summary>
    /// Computes per-prefix memory distribution from keys and their memory info.
    /// Groups by the first segment before the separator.
    /// </summary>
    public static List<PrefixMemoryBucket> ComputePrefixMemory(
        IReadOnlyList<RedisKeyInfo> keyInfos,
        string separator)
    {
        if (keyInfos.Count == 0 || string.IsNullOrEmpty(separator))
            return [];

        var buckets = new Dictionary<string, (int Count, long Bytes)>(StringComparer.Ordinal);

        foreach (var info in keyInfos)
        {
            var prefix = GetTopPrefix(info.Key, separator);
            var bytes = info.MemoryBytes ?? 0;

            if (buckets.TryGetValue(prefix, out var existing))
                buckets[prefix] = (existing.Count + 1, existing.Bytes + bytes);
            else
                buckets[prefix] = (1, bytes);
        }

        var totalBytes = buckets.Values.Sum(b => b.Bytes);

        return buckets
            .Select(kvp => new PrefixMemoryBucket
            {
                Prefix = kvp.Key,
                KeyCount = kvp.Value.Count,
                TotalBytes = kvp.Value.Bytes,
                Percentage = totalBytes > 0 ? (double)kvp.Value.Bytes / totalBytes * 100 : 0
            })
            .OrderByDescending(b => b.TotalBytes)
            .ToList();
    }

    private static string GetTopPrefix(string key, string separator)
    {
        var idx = key.IndexOf(separator, StringComparison.Ordinal);
        return idx > 0 ? key[..idx] : key;
    }
}
