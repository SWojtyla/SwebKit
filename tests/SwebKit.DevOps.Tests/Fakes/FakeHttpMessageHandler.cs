using System.Net;
using System.Text;

namespace SwebKit.DevOps.Tests.Fakes;

/// <summary>
/// A test double for <see cref="HttpMessageHandler"/> that returns pre-queued responses.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();
    private readonly List<Uri?> _requestUris = [];
    private readonly List<string?> _authorizationParameters = [];

    public IReadOnlyList<Uri?> RequestUris => _requestUris;
    public Uri? LastRequestUri => _requestUris.Count > 0 ? _requestUris[^1] : null;

    public IReadOnlyList<string?> AuthorizationParameters => _authorizationParameters;
    public string? LastAuthorizationParameter => _authorizationParameters.Count > 0
        ? _authorizationParameters[^1]
        : null;

    public void EnqueueResponse(HttpResponseMessage response) => _responses.Enqueue(response);

    public void EnqueueJson(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responses.Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requestUris.Add(request.RequestUri);
        _authorizationParameters.Add(request.Headers.Authorization?.Parameter);

        if (_responses.Count == 0)
            throw new InvalidOperationException("No more responses queued in FakeHttpMessageHandler.");

        return Task.FromResult(_responses.Dequeue());
    }
}
