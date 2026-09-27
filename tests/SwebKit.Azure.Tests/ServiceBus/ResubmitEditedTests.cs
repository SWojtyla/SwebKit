using Azure.Messaging.ServiceBus;
using SwebKit.Azure.ServiceBus;
using SwebKit.Core.Models;

namespace SwebKit.Azure.Tests.ServiceBus;

/// <summary>
/// Covers <see cref="AzureServiceBusClient.BuildEditedResubmitMessage"/> — the outbound half of
/// the resubmit-with-edit path. The fake is a <see cref="ServiceBusModelFactory"/>-built received
/// message, the same shape the DLQ receive loop hands the builder.
/// </summary>
public sealed class ResubmitEditedTests
{
    private static ServiceBusReceivedMessage FakeReceived(
        IDictionary<string, object>? properties = null,
        string? sessionId = null,
        long sequenceNumber = 4410)
    {
        return ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("""{"original":true}"""),
            messageId: "original-id",
            sessionId: sessionId,
            sequenceNumber: sequenceNumber,
            subject: "OriginalSubject",
            correlationId: "corr-original",
            contentType: "application/json",
            properties: properties ?? new Dictionary<string, object>());
    }

    private static SbMessage Edit(Action<SbMessage>? tweak = null)
    {
        var message = new SbMessage
        {
            MessageId = "edited-id",
            Subject = "EditedSubject",
            CorrelationId = "corr-edited",
            ContentType = "text/plain",
            Body = """{"edited":true}""",
            SessionId = "sess-edited",
            ApplicationProperties = new Dictionary<string, object>
            {
                ["userProp"] = "userValue",
            },
        };
        tweak?.Invoke(message);
        return message;
    }

    [Fact]
    public void BuildEditedResubmitMessage_AppliesTheEditedFieldsOntoABrokerClone()
    {
        var forwarded = AzureServiceBusClient.BuildEditedResubmitMessage(
            FakeReceived(properties: new Dictionary<string, object> { ["staleProp"] = "gone" }),
            Edit());

        Assert.Equal("edited-id", forwarded.MessageId);
        Assert.Equal("""{"edited":true}""", forwarded.Body.ToString());
        Assert.Equal("EditedSubject", forwarded.Subject);
        Assert.Equal("corr-edited", forwarded.CorrelationId);
        Assert.Equal("text/plain", forwarded.ContentType);
        Assert.Equal("sess-edited", forwarded.SessionId);
    }

    [Fact]
    public void BuildEditedResubmitMessage_ReplacesApplicationPropertiesAndStripsDlqStamp()
    {
        var original = FakeReceived(properties: new Dictionary<string, object>
        {
            ["DeadLetterReason"] = "MaxDeliveryCountExceeded",
            ["DeadLetterErrorDescription"] = "too many retries",
            ["keptProp"] = "should-not-survive",
        });

        var forwarded = AzureServiceBusClient.BuildEditedResubmitMessage(original, Edit());

        // The edited property set replaces the original wholesale — a property the user removed
        // in the composer must not reappear — and the dead-letter stamp never survives a resubmit.
        Assert.True(forwarded.ApplicationProperties.ContainsKey("userProp"));
        Assert.False(forwarded.ApplicationProperties.ContainsKey("DeadLetterReason"));
        Assert.False(forwarded.ApplicationProperties.ContainsKey("DeadLetterErrorDescription"));
        Assert.False(forwarded.ApplicationProperties.ContainsKey("keptProp"));
    }

    [Fact]
    public void BuildEditedResubmitMessage_GeneratesAMessageIdWhenTheEditLeftItBlank()
    {
        var forwarded = AzureServiceBusClient.BuildEditedResubmitMessage(
            FakeReceived(),
            Edit(m => m.MessageId = "  "));

        Assert.False(string.IsNullOrWhiteSpace(forwarded.MessageId));
        Assert.NotEqual("original-id", forwarded.MessageId);
    }

    [Fact]
    public void BuildEditedResubmitMessage_PreservesBrokerFieldsTheEditCannotModel()
    {
        var original = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: "original-id",
            replyTo: "reply-queue",
            to: "dest-queue",
            sequenceNumber: 4410);

        var forwarded = AzureServiceBusClient.BuildEditedResubmitMessage(original, Edit());

        // Fields SbMessage doesn't model ride along on the broker copy — payload fidelity, not
        // positional fidelity, is the contract.
        Assert.Equal("reply-queue", forwarded.ReplyTo);
        Assert.Equal("dest-queue", forwarded.To);
    }
}
