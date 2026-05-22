using SwebKit.Core.Domain;

namespace SwebKit.Core.Models;

public sealed class RedisConnectionImportResult
{
    public List<RedisCacheEntry> Caches { get; set; } = [];
    public string SuggestedSeparator { get; set; } = "-";
    public bool HasMixedSeparators { get; set; }
    public List<string> Warnings { get; set; } = [];
}