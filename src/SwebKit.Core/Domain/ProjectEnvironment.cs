namespace SwebKit.Core.Domain;

public class ProjectEnvironment
{
    public List<SbEntityLink> ServiceBusEntityLinks { get; set; } = [];
    public AksConfig? AksConfig { get; set; }
    public RedisConfig? RedisConfig { get; set; }
    public List<StorageConfig> StorageAccounts { get; set; } = [];
    public DevOpsConfig? DevOpsConfig { get; set; }
    public List<FavoriteEntity> FavoriteEntities { get; set; } = [];
    public Dictionary<string, FilterState> LastUsedFilters { get; set; } = [];
}
