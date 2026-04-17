using SwebKit.Core.Abstractions;

namespace SwebKit.App.Services;

public sealed class AksWarmupCache : IAksWarmupCache
{
    private AksClientBootstrapResult? _result;

    public void Store(AksClientBootstrapResult result) => _result = result;
    public AksClientBootstrapResult? TryGet() => _result;
    public void Invalidate() => _result = null;
}
