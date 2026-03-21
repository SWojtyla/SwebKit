using SwebKit.Core.Abstractions;

namespace SwebKit.Core.Tests.Fakes;

public sealed class FakeCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, string> _store = [];

    public void Save(string key, string secret) => _store[key] = secret;
    public string? Get(string key) => _store.GetValueOrDefault(key);
    public void Delete(string key) => _store.Remove(key);
    public IReadOnlyList<string> ListKeys(string prefix = "") =>
        _store.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
}
