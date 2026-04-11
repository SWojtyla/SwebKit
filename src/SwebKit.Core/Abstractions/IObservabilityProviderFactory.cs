namespace SwebKit.Core.Abstractions;

public interface IObservabilityProviderFactory
{
    IObservabilityProvider Create(string resourceId, bool useDemoData);
}