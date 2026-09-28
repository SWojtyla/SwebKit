using Azure.Messaging.ServiceBus;
using SwebKit.Azure.ServiceBus;
using SwebKit.Core.Models;

namespace SwebKit.Azure.Tests.ServiceBus;

/// <summary>
/// Covers the sessions-peek path in <see cref="AzureServiceBusClient.PeekSessionsAsync"/>: peeked
/// messages are mapped (<see cref="AzureServiceBusClient.MapMessage"/>) and grouped by session id
/// (<see cref="SbSessionSummary.Summarize"/>). The received-message fakes go through
/// <see cref="ServiceBusModelFactory"/>, the same shape a real peek returns.
/// </summary>
public sealed class SessionPeekTests
{
    private static ServiceBusReceivedMessage FakePeeked(
        string? sessionId,
        DateTimeOffset enqueued,
        long sequenceNumber)
    {
        return ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: $"m-{sequenceNumber}",
            sessionId: sessionId,
            enqueuedTime: enqueued,
            sequenceNumber: sequenceNumber);
    }

    [Fact]
    public void PeekedWindow_GroupsBySessionIdWithCountsAndEnqueueSpan()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var peeked = new[]
        {
            FakePeeked("sess-a", t0.AddMinutes(1), 1),
            FakePeeked("sess-b", t0.AddMinutes(2), 2),
            FakePeeked("sess-a", t0.AddMinutes(5), 3),
        }.Select(AzureServiceBusClient.MapMessage);

        var summaries = SbSessionSummary.Summarize(peeked);

        Assert.Equal(2, summaries.Count);
        var a = summaries.Single(s => s.SessionId == "sess-a");
        Assert.Equal(2, a.MessageCount);
        Assert.Equal(t0.AddMinutes(1), a.FirstEnqueuedAt);
        Assert.Equal(t0.AddMinutes(5), a.LastEnqueuedAt);
        // Most recently active session leads — the chip bar shows what moved last, first.
        Assert.Equal("sess-a", summaries[0].SessionId);
    }

    [Fact]
    public void PeekedWindow_SkipsSessionlessMessages()
    {
        var peeked = new[]
        {
            FakePeeked(null, DateTimeOffset.UtcNow, 1),
            FakePeeked("sess-x", DateTimeOffset.UtcNow, 2),
        }.Select(AzureServiceBusClient.MapMessage);

        var summaries = SbSessionSummary.Summarize(peeked);

        Assert.Single(summaries);
        Assert.Equal("sess-x", summaries[0].SessionId);
    }

    [Fact]
    public void PeekedWindow_EmptyPeekGroupsToNothing()
    {
        Assert.Empty(SbSessionSummary.Summarize([]));
    }
}
