namespace SwebKit.Core.Domain;

/// <summary>Global (not per-project) Service Bus namespace.</summary>
public class ServiceBusNamespace
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>User-friendly label, auto-derived from the hostname short name.</summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>e.g. sb-dev-shared-sb-weu.servicebus.windows.net</summary>
    public string FullyQualifiedNamespace { get; set; } = string.Empty;

    /// <summary>
    /// Authentication mode. Defaults to <see cref="SbAuthMode.ConnectionString"/> for backward compatibility
    /// with persisted configs that pre-date Entra support.
    /// </summary>
    public SbAuthMode AuthMode { get; set; } = SbAuthMode.ConnectionString;

    /// <summary>Key used to retrieve the connection string from ICredentialStore. Only used when <see cref="AuthMode"/> is <see cref="SbAuthMode.ConnectionString"/>.</summary>
    public string CredentialKey { get; set; } = string.Empty;

    /// <summary>
    /// Data-plane transport for this namespace. Defaults to <see cref="SbTransportType.Amqp"/> for backward
    /// compatibility with existing persisted configs. Switch to <see cref="SbTransportType.AmqpWebSockets"/>
    /// when the network path blocks plain AMQP (port 5671) but allows HTTPS (port 443).
    /// </summary>
    public SbTransportType TransportType { get; set; } = SbTransportType.Amqp;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
