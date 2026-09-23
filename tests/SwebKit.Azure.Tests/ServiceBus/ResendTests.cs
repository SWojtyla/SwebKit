using System.Text.Json;
using Azure.Messaging.ServiceBus;
using SwebKit.Azure.ServiceBus;

namespace SwebKit.Azure.Tests.ServiceBus;

/// <summary>
/// Covers the two resend helpers on <see cref="AzureServiceBusClient"/>:
/// <see cref="AzureServiceBusClient.NormalizePropertyValue"/>, the fix for the 500 every
/// send of a JSON-bound message with application properties used to hit, and
/// <see cref="AzureServiceBusClient.ResolveResendTarget"/>, the NServiceBus.FailedQ
/// resolution that routes a resend back to the queue the message failed in.
/// </summary>
public sealed class ResendTests
{
    private static object JsonValue(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, object>>(json)!["value"];

    [Fact]
    public void NormalizePropertyValue_StringJsonElement_BecomesString()
    {
        Assert.Equal("Send", AzureServiceBusClient.NormalizePropertyValue(JsonValue("""{"value":"Send"}""")));
    }

    [Fact]
    public void NormalizePropertyValue_IntegralJsonElement_BecomesLong()
    {
        Assert.Equal(42L, AzureServiceBusClient.NormalizePropertyValue(JsonValue("""{"value":42}""")));
    }

    [Fact]
    public void NormalizePropertyValue_FractionalJsonElement_BecomesDouble()
    {
        Assert.Equal(1.5d, AzureServiceBusClient.NormalizePropertyValue(JsonValue("""{"value":1.5}""")));
    }

    [Fact]
    public void NormalizePropertyValue_BooleanJsonElement_BecomesBool()
    {
        Assert.Equal(true, AzureServiceBusClient.NormalizePropertyValue(JsonValue("""{"value":true}""")));
        Assert.Equal(false, AzureServiceBusClient.NormalizePropertyValue(JsonValue("""{"value":false}""")));
    }

    [Fact]
    public void NormalizePropertyValue_NullJsonElement_BecomesNull()
    {
        Assert.Null(AzureServiceBusClient.NormalizePropertyValue(JsonValue("""{"value":null}""")));
    }

    [Fact]
    public void NormalizePropertyValue_ComplexJsonElement_BecomesRawJsonText()
    {
        // AMQP application-property maps can't carry nested values — degrade to raw JSON
        // rather than throwing on a property shape the broker cannot represent anyway.
        Assert.Equal("""{"a":1}""", AzureServiceBusClient.NormalizePropertyValue(JsonValue("""{"value":{"a":1}}""")));
    }

    [Fact]
    public void NormalizePropertyValue_NonJsonElement_PassesThroughUnchanged()
    {
        var guid = Guid.NewGuid();
        Assert.Equal(guid, AzureServiceBusClient.NormalizePropertyValue(guid));
        Assert.Equal(7L, AzureServiceBusClient.NormalizePropertyValue(7L));
    }

    private static ServiceBusReceivedMessage ReceivedWithFailedQ(object? failedQ)
    {
        var properties = new Dictionary<string, object>();
        if (failedQ is not null)
        {
            properties["NServiceBus.FailedQ"] = failedQ;
        }

        return ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: "m-1",
            properties: properties);
    }

    [Fact]
    public void ResolveResendTarget_UsesFailedQHeaderWhenPresent()
    {
        var message = ReceivedWithFailedQ("sbq-orders");
        Assert.Equal("sbq-orders", AzureServiceBusClient.ResolveResendTarget(message, "error"));
    }

    [Fact]
    public void ResolveResendTarget_StripsMsmqMachineSuffix()
    {
        var message = ReceivedWithFailedQ("sbq-orders@machine01");
        Assert.Equal("sbq-orders", AzureServiceBusClient.ResolveResendTarget(message, "error"));
    }

    [Fact]
    public void ResolveResendTarget_FallsBackToSourceEntityWithoutHeader()
    {
        Assert.Equal("error", AzureServiceBusClient.ResolveResendTarget(ReceivedWithFailedQ(null), "error"));
    }

    [Fact]
    public void ResolveResendTarget_FallsBackOnBlankOrNonStringHeader()
    {
        Assert.Equal("error", AzureServiceBusClient.ResolveResendTarget(ReceivedWithFailedQ("  "), "error"));
        Assert.Equal("error", AzureServiceBusClient.ResolveResendTarget(ReceivedWithFailedQ(42), "error"));
    }
}
