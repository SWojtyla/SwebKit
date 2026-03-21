using System.Net;
using System.Text;

namespace SwebKit.DevOps.Tests.Fakes;

/// <summary>
/// A test double for <see cref="HttpMessageHandler"/> that returns pre-queued responses.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

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
        if (_responses.Count == 0)
            throw new InvalidOperationException("No more responses queued in FakeHttpMessageHandler.");

        return Task.FromResult(_responses.Dequeue());
    }
}
