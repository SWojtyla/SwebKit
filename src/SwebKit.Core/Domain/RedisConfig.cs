namespace SwebKit.Core.Domain;

public class RedisConfig
{
    public string ConnectionString { get; set; } = string.Empty;
    public string? Alias { get; set; }
    public int Database { get; set; }
}
