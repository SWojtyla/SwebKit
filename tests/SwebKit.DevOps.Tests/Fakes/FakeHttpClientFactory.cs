namespace SwebKit.DevOps.Tests.Fakes;

/// <summary>
/// A minimal <see cref="IHttpClientFactory"/> that always returns the supplied client,
/// regardless of the requested named client.
/// </summary>
public sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpClient _client;

    public FakeHttpClientFactory(HttpClient client)
    {
        _client = client;
    }

    public HttpClient CreateClient(string name) => _client;
}
