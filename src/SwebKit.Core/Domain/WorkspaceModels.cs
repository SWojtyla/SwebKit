namespace SwebKit.Core.Domain;

public class OperatorResourceReference
{
    public string Key { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayPath { get; set; }
    public string? Summary { get; set; }
    public string? Icon { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];

    public OperatorResourceReference Clone() => new()
    {
        Key = Key,
        Area = Area,
        Kind = Kind,
        DisplayName = DisplayName,
        DisplayPath = DisplayPath,
        Summary = Summary,
        Icon = Icon,
        Metadata = Metadata.ToDictionary(static entry => entry.Key, static entry => entry.Value, StringComparer.Ordinal),
    };
}

public class WorkspaceSnapshot
{
    public OperatorResourceReference Resource { get; set; } = new();
    public Dictionary<string, string> RestoreState { get; set; } = [];
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    public WorkspaceSnapshot Clone() => new()
    {
        Resource = Resource.Clone(),
        RestoreState = RestoreState.ToDictionary(static entry => entry.Key, static entry => entry.Value, StringComparer.Ordinal),
        CapturedAt = CapturedAt,
    };
}

public class FavoriteResource
{
    public WorkspaceSnapshot Snapshot { get; set; } = new();
    public DateTimeOffset PinnedAt { get; set; } = DateTimeOffset.UtcNow;

    public FavoriteResource Clone() => new()
    {
        Snapshot = Snapshot.Clone(),
        PinnedAt = PinnedAt,
    };
}

public class RecentResourceEntry
{
    public WorkspaceSnapshot Snapshot { get; set; } = new();
    public DateTimeOffset AccessedAt { get; set; } = DateTimeOffset.UtcNow;

    public RecentResourceEntry Clone() => new()
    {
        Snapshot = Snapshot.Clone(),
        AccessedAt = AccessedAt,
    };
}

public class SavedWorkspace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public WorkspaceSnapshot Snapshot { get; set; } = new();
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
    public int SchemaVersion { get; set; } = 1;

    public SavedWorkspace Clone() => new()
    {
        Id = Id,
        Name = Name,
        Snapshot = Snapshot.Clone(),
        SavedAt = SavedAt,
        SchemaVersion = SchemaVersion,
    };
}