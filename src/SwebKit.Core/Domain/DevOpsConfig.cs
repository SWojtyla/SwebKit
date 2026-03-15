namespace SwebKit.Core.Domain;

public class DevOpsConfig
{
    public string Organization { get; set; } = string.Empty;

    /// <summary>Key in ICredentialStore for the PAT. Never logged or exposed in UI.</summary>
    public string PatCredentialKey { get; set; } = string.Empty;
}
