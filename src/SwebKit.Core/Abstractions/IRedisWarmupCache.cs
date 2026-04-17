using SwebKit.Core.Abstractions;

namespace SwebKit.Core.Abstractions;

public interface IRedisWarmupCache
{
    void Store(string cacheId, IRedisClient client);
    IRedisClient? TryGet(string cacheId);
    void Invalidate();
}
