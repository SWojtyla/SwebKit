using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IConfigurationHealthService
{
    ConfigurationHealthReport BuildReport(ConfigurationHealthContext context);
}

public interface IConfigurationProbeService
{
    ConfigurationProbeSnapshot? GetLatest(ConfigurationHealthContext context);

    Task<ConfigurationProbeSnapshot> RunAsync(ConfigurationHealthContext context, CancellationToken ct = default);

    void Invalidate();
}