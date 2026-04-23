using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Azure.Storage;

public sealed class StorageClientFactory(ICredentialStore credentialStore) : IStorageClientFactory
{
    public IStorageClient Create(StorageConfig config) => new AzureStorageClient(config, credentialStore);
}