using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Models;

public sealed class ConfigurationBundle
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset ExportedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ProfileData Profiles { get; set; } = new();
    public UiState UiState { get; set; } = new();
    public UserSettings UserSettings { get; set; } = new();
    public ReleaseStoreData Releases { get; set; } = new();
    public List<ScheduledMessageEntry> ScheduledMessages { get; set; } = [];
}