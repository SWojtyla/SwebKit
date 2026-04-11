using SwebKit.Core.Domain;

namespace SwebKit.Core.Abstractions;

public interface IDevOpsClientFactory
{
    IDevOpsClient Create(DevOpsConfig config);
}