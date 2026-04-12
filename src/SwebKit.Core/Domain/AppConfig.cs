namespace SwebKit.Core.Domain;

public class AppConfig
{
    public string Name { get; set; } = "Default";
    public bool IsProduction { get; set; }
    public IncidentTimelineConfig IncidentTimeline { get; set; } = new();
    public List<SbEntityLink> ServiceBusEntityLinks { get; set; } = [];
    public AksConfig? AksConfig { get; set; }
    public RedisConfig? RedisConfig { get; set; }
    public List<StorageConfig> StorageAccounts { get; set; } = [];
    public DevOpsConfig? DevOpsConfig { get; set; }
    public ObservabilityConfig? ObservabilityConfig { get; set; }
    public List<FavoriteEntity> FavoriteEntities { get; set; } = [];
    public Dictionary<string, FilterState> LastUsedFilters { get; set; } = [];
}
