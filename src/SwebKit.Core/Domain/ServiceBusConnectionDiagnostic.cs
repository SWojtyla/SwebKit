namespace SwebKit.Core.Domain;

/// <summary>
/// Non-secret identifiers describing which credential and endpoint were used for a Service Bus
/// connection attempt, so a misconfiguration can be diagnosed without exposing secret material.
/// </summary>
/// <remarks>
/// SECURITY (DEC-3) — hard rule: this type must NEVER carry the SAS key value, the full connection
/// string, or a token. It exposes only identifiers that are safe to display in the UI and to log:
/// the endpoint host, the SAS key <em>name</em>, the auth method, and the credential-source label
/// (the secret-reference name / config key that resolved the connection string).
/// </remarks>
public sealed record ServiceBusConnectionDiagnostic(
    string EndpointHost,
    string? SharedAccessKeyName,
    string AuthMethod,
    string CredentialSource);
