namespace SwebKit.Core.Abstractions;

public interface ICredentialStore
{
    void Save(string key, string secret);
    string? Get(string key);
    void Delete(string key);
    IReadOnlyList<string> ListKeys(string prefix = "");
}
