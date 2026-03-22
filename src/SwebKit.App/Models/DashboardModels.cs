namespace SwebKit.App;

/// <summary>Snapshot health metric shown on a dashboard health tile.</summary>
public record HealthTileData(int Value, string Label, DateTimeOffset LastUpdated);
