namespace SwebKit.Core.Domain;

public class FavoriteEntity
{
    public EntityType EntityType { get; set; }
    public required string Name { get; set; }
    public string? ParentName { get; set; }
    public DateTimeOffset PinnedAt { get; set; } = DateTimeOffset.UtcNow;

    public string DisplayPath => ParentName is null ? Name : $"{ParentName}/{Name}";
}
