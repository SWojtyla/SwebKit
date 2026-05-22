namespace SwebKit.Core.Models;

public sealed class RedisImportEntry
{
    public string Key { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public string? StringValue { get; set; }
    public Dictionary<string, string> HashFields { get; set; } = [];
    public List<string> ListItems { get; set; } = [];
    public List<string> SetMembers { get; set; } = [];
    public List<RedisSortedSetEntry> SortedSetMembers { get; set; } = [];
    public TimeSpan? Ttl { get; set; }
}

public sealed class RedisImportResult
{
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<string> Warnings { get; } = [];
}