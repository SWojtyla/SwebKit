using SwebKit.Core.Abstractions;

namespace SwebKit.Core.Abstractions;

public interface IAksWarmupCache
{
    void Store(AksClientBootstrapResult result);
    AksClientBootstrapResult? TryGet();
    void Invalidate();
}
