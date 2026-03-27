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
}
