using SwebKit.Core.Models;

namespace SwebKit.Core.Tests;

/// <summary>
/// <see cref="SbReplay.BuildClone"/> — the shared clone-builder both the Azure and demo replay
/// paths send through. The contract: payload survives (body/subject/correlation id/content type),
/// broker-assigned fields do NOT (sequence, enqueue time, delivery count — those are the target's
/// to assign), DLQ headers never ride a replayed copy, and provenance stamps land AFTER any scrub.
/// </summary>
public class SbReplayCloneTests
{
    private static SbMessage Source() => new()
    {
        MessageId = "src-1",
        Subject = "OrderCreated",
        CorrelationId = "corr-1",
        ContentType = "application/json",
        Body = """{"orderId":"ORD-1"}""",
        SessionId = "sess-alpha",
        SequenceNumber = 4501,
        DeliveryCount = 7,
        DeadLetterReason = "MaxDeliveryCountExceeded",
        DeadLetterErrorDescription = "gave up",
        EnqueuedAt = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero),
        ApplicationProperties = new Dictionary<string, object>
        {
            ["NServiceBus.MessageId"] = "nsb-1",
            ["DeadLetterReason"] = "MaxDeliveryCountExceeded",
            ["DeadLetterErrorDescription"] = "gave up",
            [SbRequeueStamp.ParkedRole] = "prefix",   // a prior op's stamp
        },
    };

    private static SbReplayOptions Options(bool scrub = false, bool strip = false) => new()
    {
        ScrubApplicationProperties = scrub,
        StripSessionId = strip,
        ReplayedFrom = "orders-dev.servicebus.windows.net/order-created",
        OperationId = "op-1",
    };

    [Fact]
    public void Clone_PreservesPayload_ClearsBrokerFields_StampsProvenance()
    {
        var clone = SbReplay.BuildClone(Source(), Options());

        Assert.Equal(Source().Body, clone.Body);
        Assert.Equal("OrderCreated", clone.Subject);
        Assert.Equal("corr-1", clone.CorrelationId);
        Assert.Equal("application/json", clone.ContentType);
        Assert.NotEqual("src-1", clone.MessageId);    // fresh id — a new message

        // Broker-assigned/lost-by-design fields — null on the SbMessage, assigned by the target.
        Assert.Null(clone.SequenceNumber);
        Assert.Null(clone.DeadLetterReason);
        Assert.Null(clone.DeadLetterErrorDescription);

        Assert.Equal("orders-dev.servicebus.windows.net/order-created",
            clone.ApplicationProperties[SbReplayStamp.ReplayedFrom]);
        Assert.Equal("op-1", clone.ApplicationProperties[SbReplayStamp.OperationId]);
        // Original position is recorded as provenance, never as an attempt to restore order.
        Assert.Equal(4501L, clone.ApplicationProperties[SbRequeueStamp.OriginalSequence]);
        Assert.Equal(7, clone.ApplicationProperties[SbRequeueStamp.OriginalDeliveryCount]);
    }

    [Fact]
    public void Clone_WithoutScrub_KeepsAppProperties_ButDropsDlqHeaders()
    {
        var clone = SbReplay.BuildClone(Source(), Options(scrub: false));

        Assert.Equal("nsb-1", clone.ApplicationProperties["NServiceBus.MessageId"]);
        Assert.False(clone.ApplicationProperties.ContainsKey("DeadLetterReason"));
        Assert.False(clone.ApplicationProperties.ContainsKey("DeadLetterErrorDescription"));
    }

    [Fact]
    public void Clone_WithScrub_DropsAllAppProperties_ExceptProvenance()
    {
        var clone = SbReplay.BuildClone(Source(), Options(scrub: true));

        Assert.False(clone.ApplicationProperties.ContainsKey("NServiceBus.MessageId"));
        Assert.False(clone.ApplicationProperties.ContainsKey(SbRequeueStamp.ParkedRole));
        // Provenance survives the scrub — it's applied after.
        Assert.Equal("orders-dev.servicebus.windows.net/order-created",
            clone.ApplicationProperties[SbReplayStamp.ReplayedFrom]);
        Assert.Equal(4501L, clone.ApplicationProperties[SbRequeueStamp.OriginalSequence]);
    }

    [Fact]
    public void Clone_StripSessionId_DropsSession_KeepsOtherwise()
    {
        Assert.Null(SbReplay.BuildClone(Source(), Options(strip: true)).SessionId);
        Assert.Equal("sess-alpha", SbReplay.BuildClone(Source(), Options(strip: false)).SessionId);
    }

    [Fact]
    public void Clone_DoesNotMutateSource()
    {
        var source = Source();
        _ = SbReplay.BuildClone(source, Options(scrub: true));

        Assert.True(source.ApplicationProperties.ContainsKey("NServiceBus.MessageId"));
        Assert.True(source.ApplicationProperties.ContainsKey("DeadLetterReason"));
    }
}
