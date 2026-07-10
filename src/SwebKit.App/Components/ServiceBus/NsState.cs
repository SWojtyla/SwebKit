using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.App.Components.ServiceBus;

public class NsState
{
    public required ServiceBusNamespace Namespace { get; set; }
    public IServiceBusClient? Client { get; set; }
    public bool IsExpanded { get; set; }
    public bool IsConnecting { get; set; }
    public string? ConnectionError { get; set; }

    /// <summary>
    /// Non-secret diagnostic identifiers (endpoint host, SAS key name, auth method, credential source)
    /// captured on the connect path so the UI can explain which credential/endpoint was used. Never
    /// contains secret material (DEC-3).
    /// </summary>
    public ServiceBusConnectionDiagnostic? ConnectionDiagnostic { get; set; }

    /// <summary>True when the connection failure is an authentication/authorization problem.</summary>
    public bool IsAuthFailure { get; set; }
}
