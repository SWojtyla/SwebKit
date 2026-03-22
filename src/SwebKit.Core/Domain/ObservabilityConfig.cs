namespace SwebKit.Core.Domain;

public class ObservabilityConfig
{
    /// <summary>Azure App Insights resource ID of the last selected resource.</summary>
    public string? SelectedResourceId { get; set; }

    /// <summary>Display name cached for the last selected resource (avoids a discovery call on startup).</summary>
    public string? SelectedResourceName { get; set; }

    /// <summary>Maximum rows returned per query. Caps cost and prevents UI freezes.</summary>
    public int MaxRowsPerQuery { get; set; } = 500;

    public List<SavedQuery> SavedQueries { get; set; } = [];
}

public class SavedQuery
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
