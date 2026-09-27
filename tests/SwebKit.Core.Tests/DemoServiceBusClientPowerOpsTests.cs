using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

/// <summary>
/// Power-ops slice coverage for <see cref="DemoServiceBusClient"/>: session awareness and
/// move-semantics honesty. Demo mutations must genuinely mutate the store — an e2e run that
/// "passes" against a no-op resubmit is a false positive, so these tests pin the store changes.
/// </summary>
public sealed class DemoServiceBusClientPowerOpsTests
{
    // ── Session awareness ────────────────────────────────────────────────────

    [Fact]
    public async Task ListQueuesAsync_FlagsTheSessionRequiredQueue()
    {
        var client = DemoServiceBusClient.OrdersDev();

        var queues = await client.ListQueuesAsync();

        Assert.True(queues.Single(q => q.EntityPath == "order-sessions").RequiresSession);
        Assert.All(queues.Where(q => q.EntityPath != "order-sessions"), q => Assert.False(q.RequiresSession));
    }

    [Fact]
    public async Task PeekSessionsAsync_GroupsThePeekWindowBySessionId()
    {
        var client = DemoServiceBusClient.OrdersDev();

        var sessions = await client.PeekSessionsAsync("order-sessions", 32);

        Assert.Equal(3, sessions.Count);
        Assert.Equal(2, sessions.Single(s => s.SessionId == "sess-alpha").MessageCount);
        Assert.Equal(2, sessions.Single(s => s.SessionId == "sess-beta").MessageCount);
        Assert.Equal(1, sessions.Single(s => s.SessionId == "sess-gamma").MessageCount);

        foreach (var summary in sessions)
        {
            Assert.True(summary.FirstEnqueuedAt <= summary.LastEnqueuedAt);
        }
    }

    [Fact]
    public async Task PeekSessionsAsync_HonorsThePeekWindowCount()
    {
        var client = DemoServiceBusClient.OrdersDev();

        // Only the first two active messages are visible — both sess-alpha.
        var sessions = await client.PeekSessionsAsync("order-sessions", 2);

        Assert.Single(sessions);
        Assert.Equal("sess-alpha", sessions[0].SessionId);
        Assert.Equal(2, sessions[0].MessageCount);
    }

    [Fact]
    public async Task PeekSessionsAsync_NonSessionEntity_ReturnsEmpty()
    {
        var client = DemoServiceBusClient.OrdersDev();

        var sessions = await client.PeekSessionsAsync("order-created", 32);

        Assert.Empty(sessions);
    }

    // ── Move-semantics honesty ───────────────────────────────────────────────

    [Fact]
    public async Task ResubmitDeadLetterAsync_MovesMessageFromDlqToTarget()
    {
        var client = DemoServiceBusClient.OrdersDev();
        var dlqBefore = await client.PeekDeadLetterAsync("order-created", 10);
        var targetBefore = await client.PeekMessagesAsync("order-processed", 32);

        await client.ResubmitDeadLetterAsync("order-created", ["4410"], "order-processed");

        var dlqAfter = await client.PeekDeadLetterAsync("order-created", 10);
        var targetAfter = await client.PeekMessagesAsync("order-processed", 32);

        Assert.Equal(dlqBefore.Count - 1, dlqAfter.Count);
        Assert.DoesNotContain(dlqAfter, m => m.SequenceNumber == 4410);
        Assert.Equal(targetBefore.Count + 1, targetAfter.Count);
        var moved = targetAfter.Single(m => m.Body.Contains("ORD-12200"));
        Assert.NotEqual("oc-dlq-001", moved.MessageId); // fresh id — a clone, not the original
        Assert.Null(moved.DeadLetterReason);
        Assert.Null(moved.DeadLetterErrorDescription);
    }

    [Fact]
    public async Task ResubmitDeadLetterAsync_UnknownSequence_Throws()
    {
        var client = DemoServiceBusClient.OrdersDev();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ResubmitDeadLetterAsync("order-created", ["999999"], null));
    }

    [Fact]
    public async Task CompleteDeadLetterAsync_RemovesMessagesFromDlq()
    {
        var client = DemoServiceBusClient.OrdersDev();
        var before = await client.PeekDeadLetterAsync("order-created", 10);

        await client.CompleteDeadLetterAsync("order-created", ["4388", "4350"]);

        var after = await client.PeekDeadLetterAsync("order-created", 10);
        Assert.Equal(before.Count - 2, after.Count);
        Assert.DoesNotContain(after, m => m.SequenceNumber is 4388 or 4350);
    }

    [Fact]
    public async Task CompleteDeadLetterAsync_UnknownSequence_Throws()
    {
        var client = DemoServiceBusClient.OrdersDev();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.CompleteDeadLetterAsync("order-created", ["999999"]));
    }

    [Fact]
    public async Task ResubmitEditedDeadLetterAsync_SettlesOriginalAndLandsEditedCopy()
    {
        var client = DemoServiceBusClient.OrdersDev();
        var dlqBefore = await client.PeekDeadLetterAsync("order-created", 10);
        var targetBefore = await client.PeekMessagesAsync("order-created", 32);
        var edited = new SbMessage
        {
            MessageId = "edited-msg",
            Subject = "EditedSubject",
            CorrelationId = "corr-edited",
            ContentType = "application/json",
            Body = """{"orderId":"ORD-12200","fixed":true}""",
            ApplicationProperties = new Dictionary<string, object> { ["source"] = "manual-edit" },
        };

        await client.ResubmitEditedDeadLetterAsync("order-created", 4410, edited, null);

        var dlqAfter = await client.PeekDeadLetterAsync("order-created", 10);
        var targetAfter = await client.PeekMessagesAsync("order-created", 32);

        // The duplicate trap is gone: the original left the DLQ and exactly one edited copy landed.
        Assert.Equal(dlqBefore.Count - 1, dlqAfter.Count);
        Assert.DoesNotContain(dlqAfter, m => m.SequenceNumber == 4410);
        Assert.Equal(targetBefore.Count + 1, targetAfter.Count);
        var landed = targetAfter.Single(m => m.MessageId == "edited-msg");
        Assert.Equal("EditedSubject", landed.Subject);
        Assert.Equal("""{"orderId":"ORD-12200","fixed":true}""", landed.Body);
        Assert.True(landed.ApplicationProperties.ContainsKey("source"));
    }

    [Fact]
    public async Task ResubmitEditedDeadLetterAsync_UnknownSequence_ThrowsAndMutatesNothing()
    {
        var client = DemoServiceBusClient.OrdersDev();
        var edited = new SbMessage { MessageId = "m", Body = "{}" };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ResubmitEditedDeadLetterAsync("order-created", 999999, edited, null));

        var dlqAfter = await client.PeekDeadLetterAsync("order-created", 10);
        Assert.Equal(3, dlqAfter.Count);
        var activeAfter = await client.PeekMessagesAsync("order-created", 32);
        Assert.Equal(5, activeAfter.Count);
    }

    [Fact]
    public async Task ResubmitEditedDeadLetterAsync_SubscriptionTarget_DefaultsToParentTopic()
    {
        var client = DemoServiceBusClient.OrdersDev();
        var edited = new SbMessage { MessageId = "m", Body = "{}" };
        var dlqBefore = await client.PeekDeadLetterAsync("user-events/subscriptions/consumer-a", 10);
        if (dlqBefore.Count == 0)
        {
            // consumer-a's DLQ is empty in seed data; dead-letter one first so there's a sequence to edit-resubmit.
            var active = await client.PeekMessagesAsync("user-events/subscriptions/consumer-a", 10);
            await client.DeadLetterMessagesAsync("user-events/subscriptions/consumer-a", [active.First().SequenceNumber!.Value]);
            dlqBefore = await client.PeekDeadLetterAsync("user-events/subscriptions/consumer-a", 10);
        }

        var seq = dlqBefore.First().SequenceNumber!.Value;
        await client.ResubmitEditedDeadLetterAsync("user-events/subscriptions/consumer-a", seq, edited, null);

        // The sendable fallback is the topic — the subscription itself rejects sends.
        var topicAfter = await client.PeekMessagesAsync("user-events", 32);
        Assert.Contains(topicAfter, m => m.MessageId == "m");
    }
}
