using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.DevOps;

public sealed class DevOpsClientFactory : IDevOpsClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;

    public DevOpsClientFactory(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
    }

    public IDevOpsClient Create(DevOpsConfig config) =>
        new DevOpsClient(_httpClientFactory, config, _loggerFactory.CreateLogger<DevOpsClient>());
}