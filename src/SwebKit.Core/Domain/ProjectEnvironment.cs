namespace SwebKit.Core.Domain;

public class ProjectEnvironment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public required string Name { get; set; }
    public EnvironmentTier Tier { get; set; } = EnvironmentTier.NonProd;
    /// <summary>Queues/topics from global namespaces that are pinned to this environment.</summary>
    public List<SbEntityLink> ServiceBusEntityLinks { get; set; } = [];
    public ObservabilityConfig? ObservabilityConfig { get; set; }
    public AksConfig? AksConfig { get; set; }
    public List<FavoriteEntity> FavoriteEntities { get; set; } = [];
    public List<SavedQuery> SavedQueries { get; set; } = [];
    public Dictionary<string, FilterState> LastUsedFilters { get; set; } = [];

    public bool IsProduction => Tier == EnvironmentTier.Production;

    public string EnvColor => Name.ToUpperInvariant() switch
    {
        "PROD" or "PRODUCTION" or "LIVE" => "#C8002A",
        "ACC" or "ACCEPTANCE" or "STAGING" => "#8B4500",
        "TEST" or "QA" => "#004E8C",
        _ => "#1E1E2E"
    };
}
