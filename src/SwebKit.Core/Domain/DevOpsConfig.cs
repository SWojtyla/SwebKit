namespace SwebKit.Core.Domain;

public class DevOpsConfig
{
    public string Organization { get; set; } = string.Empty;

    /// <summary>Key in ICredentialStore for the PAT. Never logged or exposed in UI.</summary>
    public string PatCredentialKey { get; set; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Organization))
            throw new InvalidOperationException($"{nameof(DevOpsConfig)}.{nameof(Organization)} is required.");
        if (string.IsNullOrWhiteSpace(PatCredentialKey))
            throw new InvalidOperationException($"{nameof(DevOpsConfig)}.{nameof(PatCredentialKey)} is required.");
    }
}
