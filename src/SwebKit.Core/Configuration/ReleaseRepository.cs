using System.Text.Json;
using SwebKit.Core.Models;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

public class ReleaseRepository
{
    private static readonly JsonSerializerOptions Options = SwebKitJsonOptions.Indented;

    private List<ReleaseRecord> _releases = [];
    private List<DeploymentSnapshot> _snapshots = [];

    public IReadOnlyList<ReleaseRecord> AllReleases => _releases;
    public IReadOnlyList<DeploymentSnapshot> AllSnapshots => _snapshots;

    public async Task LoadAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        if (!AppDataFileStore.Exists(AppDataPaths.ReleasesJson)) return;

        try
        {
            var loadResult = await AppDataFileStore.LoadAsync(AppDataPaths.ReleasesJson, DeserializeStoreData);
            var data = loadResult.Value;
            _releases = data?.Releases ?? [];
            _snapshots = data?.Snapshots ?? [];
        }
        catch
        {
            _releases = [];
            _snapshots = [];
        }
    }

    public async Task SaveAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var data = new ReleaseStoreData { Releases = _releases, Snapshots = _snapshots };
        var json = JsonSerializer.Serialize(data, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.ReleasesJson, json);
    }

    public async Task AddReleaseAsync(ReleaseRecord release)
    {
        _releases.Add(release);
        await SaveAsync();
    }

    public async Task UpdateReleaseAsync(ReleaseRecord release)
    {
        var index = _releases.FindIndex(r => r.Id == release.Id);
        if (index >= 0) _releases[index] = release;
        await SaveAsync();
    }

    public async Task RemoveReleaseAsync(Guid id)
    {
        _releases.RemoveAll(r => r.Id == id);
        _snapshots.RemoveAll(s => s.ReleaseId == id);
        await SaveAsync();
    }

    public ReleaseRecord? GetRelease(Guid id) =>
        _releases.FirstOrDefault(r => r.Id == id);

    public async Task AddSnapshotsAsync(IEnumerable<DeploymentSnapshot> snapshots)
    {
        _snapshots.AddRange(snapshots);
        await SaveAsync();
    }

    public IReadOnlyList<DeploymentSnapshot> GetSnapshots(Guid releaseId) =>
        _snapshots.Where(s => s.ReleaseId == releaseId).ToList();

    private class ReleaseStoreData
    {
        public List<ReleaseRecord> Releases { get; set; } = [];
        public List<DeploymentSnapshot> Snapshots { get; set; } = [];
    }

    private static ReleaseStoreData? DeserializeStoreData(string json) =>
        JsonSerializer.Deserialize<ReleaseStoreData>(json, Options);
}
