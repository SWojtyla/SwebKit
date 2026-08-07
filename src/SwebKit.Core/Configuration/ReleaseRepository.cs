using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Models;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

public class ReleaseRepository(ILogger<ReleaseRepository>? logger = null)
{
    private static readonly JsonSerializerOptions Options = SwebKitJsonOptions.Indented;

    private List<ReleaseRecord> _releases = [];
    private List<DeploymentSnapshot> _snapshots = [];
    private List<DeploymentValidationSnapshot> _validationSnapshots = [];
    private List<ReleaseTrainRecord> _releaseTrains = [];

    public IReadOnlyList<ReleaseRecord> AllReleases => _releases;
    public IReadOnlyList<DeploymentSnapshot> AllSnapshots => _snapshots;
    public IReadOnlyList<DeploymentValidationSnapshot> AllValidationSnapshots => _validationSnapshots;
    public IReadOnlyList<ReleaseTrainRecord> AllReleaseTrains => _releaseTrains;

    public ReleaseStoreData GetStoreData() => new()
    {
        Releases = [.. _releases],
        Snapshots = [.. _snapshots],
        ValidationSnapshots = [.. _validationSnapshots],
        ReleaseTrains = [.. _releaseTrains]
    };

    public async Task LoadAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        if (!AppDataFileStore.Exists(AppDataPaths.ReleasesJson)) return;

        try
        {
            var loadResult = await AppDataFileStore.LoadAsync(AppDataPaths.ReleasesJson, DeserializeStoreData).ConfigureAwait(false);
            var data = loadResult.Value;
            _releases = data?.Releases ?? [];
            _snapshots = data?.Snapshots ?? [];
            _validationSnapshots = data?.ValidationSnapshots ?? [];
            _releaseTrains = data?.ReleaseTrains ?? [];
        }
        catch (Exception ex)
        {
            var preserved = AppDataFileStore.PreserveUnreadableFile(AppDataPaths.ReleasesJson);
            var snapshotPath = AppDataFileStore.GetUnreadableSnapshotPath(AppDataPaths.ReleasesJson);
            if (preserved)
                logger?.LogWarning(ex, "Failed to load releases from '{File}'; the file was preserved at '{Snapshot}' instead of being overwritten. Falling back to empty release data for this session.",
                    AppDataPaths.ReleasesJson, snapshotPath);
            else
                logger?.LogWarning(ex, "Failed to load releases from '{File}'; WARNING: snapshot copy failed — the next save may overwrite the original file. Falling back to empty release data for this session.",
                    AppDataPaths.ReleasesJson);
            _releases = [];
            _snapshots = [];
            _validationSnapshots = [];
            _releaseTrains = [];
        }
    }

    public async Task SaveAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var data = new ReleaseStoreData
        {
            Releases = _releases,
            Snapshots = _snapshots,
            ValidationSnapshots = _validationSnapshots,
            ReleaseTrains = _releaseTrains
        };
        var json = JsonSerializer.Serialize(data, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.ReleasesJson, json).ConfigureAwait(false);
    }

    public void ReplaceStoreData(ReleaseStoreData? data)
    {
        _releases = data?.Releases ?? [];
        _snapshots = data?.Snapshots ?? [];
        _validationSnapshots = data?.ValidationSnapshots ?? [];
        _releaseTrains = data?.ReleaseTrains ?? [];
    }

    public async Task ImportAsync(ReleaseStoreData? data)
    {
        ReplaceStoreData(data);
        await SaveAsync().ConfigureAwait(false);
    }

    public async Task AddReleaseAsync(ReleaseRecord release)
    {
        _releases.Add(release);
        await SaveAsync().ConfigureAwait(false);
    }

    public async Task UpdateReleaseAsync(ReleaseRecord release)
    {
        var index = _releases.FindIndex(r => r.Id == release.Id);
        if (index >= 0) _releases[index] = release;
        await SaveAsync().ConfigureAwait(false);
    }

    public async Task RemoveReleaseAsync(Guid id)
    {
        _releases.RemoveAll(r => r.Id == id);
        _snapshots.RemoveAll(s => s.ReleaseId == id);
        _validationSnapshots.RemoveAll(v => v.ReleaseId == id);
        await SaveAsync().ConfigureAwait(false);
    }

    public ReleaseRecord? GetRelease(Guid id) =>
        _releases.FirstOrDefault(r => r.Id == id);

    public async Task AddSnapshotsAsync(IEnumerable<DeploymentSnapshot> snapshots)
    {
        _snapshots.AddRange(snapshots);
        await SaveAsync().ConfigureAwait(false);
    }

    public IReadOnlyList<DeploymentSnapshot> GetSnapshots(Guid releaseId) =>
        _snapshots.Where(s => s.ReleaseId == releaseId).ToList();

    public async Task AddValidationSnapshotAsync(DeploymentValidationSnapshot snapshot)
    {
        _validationSnapshots.Add(snapshot);
        await SaveAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Returns persisted validation snapshots for a release, optionally filtered to one component,
    /// ordered newest-first.
    /// </summary>
    public IReadOnlyList<DeploymentValidationSnapshot> GetValidationSnapshots(
        Guid releaseId, string? componentName = null) =>
        _validationSnapshots
            .Where(v => v.ReleaseId == releaseId
                && (componentName is null || v.ComponentName == componentName))
            .OrderByDescending(v => v.ValidatedAt)
            .ToList();

    // ── Release trains ───────────────────────────────────────────────────────

    public async Task AddReleaseTrainAsync(ReleaseTrainRecord train)
    {
        _releaseTrains.Add(train);
        await SaveAsync().ConfigureAwait(false);
    }

    public async Task UpdateReleaseTrainAsync(ReleaseTrainRecord train)
    {
        var index = _releaseTrains.FindIndex(t => t.Id == train.Id);
        if (index >= 0) _releaseTrains[index] = train;
        await SaveAsync().ConfigureAwait(false);
    }

    public async Task RemoveReleaseTrainAsync(Guid id)
    {
        _releaseTrains.RemoveAll(t => t.Id == id);
        await SaveAsync().ConfigureAwait(false);
    }

    public ReleaseTrainRecord? GetReleaseTrain(Guid id) =>
        _releaseTrains.FirstOrDefault(t => t.Id == id);

    private static ReleaseStoreData? DeserializeStoreData(string json) =>
        JsonSerializer.Deserialize<ReleaseStoreData>(json, Options);
}

public sealed class ReleaseStoreData
{
    public List<ReleaseRecord> Releases { get; set; } = [];
    public List<DeploymentSnapshot> Snapshots { get; set; } = [];
    public List<DeploymentValidationSnapshot> ValidationSnapshots { get; set; } = [];
    public List<ReleaseTrainRecord> ReleaseTrains { get; set; } = [];
}
