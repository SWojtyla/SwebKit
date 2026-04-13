using SwebKit.Core.Models;

namespace SwebKit.Core.Abstractions;

public interface IConfigurationHealthService
{
    ConfigurationHealthReport BuildReport(ConfigurationHealthContext context);
}