using Bunit;
using Bunit.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Components.ServiceBus;
using SwebKit.App.Services;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.App.Tests;

public sealed class MessageDetailPaneTests : TestContext
{
    public MessageDetailPaneTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);

        Services.AddFluentUIComponents();

        var uiState = new UiStateRepository();
        Services.AddSingleton(new AppStateService(new ProfileRepository(), uiState, new AppEventBus(NullLogger<AppEventBus>.Instance)));
        Services.AddSingleton<INotificationService>(_ => new NotificationService(uiState));
        // IncidentInvestigationLauncher requires NavigationManager; bUnit provides FakeNavigationManager automatically
        Services.AddSingleton(sp => new IncidentInvestigationLauncher(sp.GetRequiredService<NavigationManager>()));
    }

    // ── Null / empty state ──────────────────────────────────────────────────

    [Fact]
    public void NoMessage_ShowsEmptyState()
    {
        var cut = RenderComponent<MessageDetailPane>(ps => ps
            .Add(p => p.Message, null));

        Assert.Contains("Select a message", cut.Markup);
    }

    // ── System tab enrichments ─────────────────────────────────────────────

    [Fact]
    public void SystemTab_ShowsPartitionKey_WhenSet()
    {
        var msg = MakeMessage(partitionKey: "pk-east");

        var cut = RenderComponent<MessageDetailPane>(ps => ps
            .Add(p => p.Message, msg));

        Assert.Contains("pk-east", cut.Markup);
    }

    [Fact]
    public void SystemTab_ShowsExpiresAt_WithRemainingLabel()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddHours(3).AddMinutes(10);
        var msg = MakeMessage(expiresAt: expiresAt);

        var cut = RenderComponent<MessageDetailPane>(ps => ps
            .Add(p => p.Message, msg));

        // Should show the date and the remaining-time cue
        Assert.Contains("remaining", cut.Markup);
    }

    [Fact]
    public void SystemTab_ShowsExpired_WhenExpiresAtIsInPast()
    {
        var msg = MakeMessage(expiresAt: DateTimeOffset.UtcNow.AddHours(-1));

        var cut = RenderComponent<MessageDetailPane>(ps => ps
            .Add(p => p.Message, msg));

        Assert.Contains("expired", cut.Markup);
    }

    [Fact]
    public void SystemTab_ShowsLockedUntil_WhenSet()
    {
        var lockedUntil = DateTimeOffset.UtcNow.AddMinutes(5);
        var msg = MakeMessage(lockedUntil: lockedUntil);

        var cut = RenderComponent<MessageDetailPane>(ps => ps
            .Add(p => p.Message, msg));

        Assert.Contains("LockedUntil", cut.Markup);
        Assert.Contains(lockedUntil.ToString("yyyy"), cut.Markup);
    }

    // ── DLQ tab enrichments ────────────────────────────────────────────────

    [Fact]
    public void DlqTab_ShowsReason_AndDescription_WhenDeadLettered()
    {
        var msg = MakeMessage(
            deadLetterReason: "MaxDeliveryCountExceeded",
            deadLetterErrorDescription: "Delivery count 10 exceeded max of 10");

        var cut = RenderComponent<MessageDetailPane>(ps => ps
            .Add(p => p.Message, msg));

        Assert.Contains("MaxDeliveryCountExceeded", cut.Markup);
        Assert.Contains("Delivery count 10 exceeded max of 10", cut.Markup);
    }

    [Fact]
    public void DlqTab_ShowsDeliveryCount_InMeta()
    {
        var msg = MakeMessage(deadLetterReason: "SomePoisonReason", deliveryCount: 7);

        var cut = RenderComponent<MessageDetailPane>(ps => ps
            .Add(p => p.Message, msg));

        Assert.Contains("Delivery count: 7", cut.Markup);
    }

    [Fact]
    public void DlqTab_NotRendered_WhenNoDeadLetterReason()
    {
        var msg = MakeMessage();

        var cut = RenderComponent<MessageDetailPane>(ps => ps
            .Add(p => p.Message, msg));

        Assert.DoesNotContain("DLQ Info", cut.Markup);
    }

    // ── Trace Pivot tab ────────────────────────────────────────────────────

    [Fact]
    public void TracePivotTab_IsAlwaysPresent_WhenMessageIsSet()
    {
        var cut = RenderComponent<MessageDetailPane>(ps => ps
            .Add(p => p.Message, MakeMessage()));

        Assert.Contains("Trace Pivot", cut.Markup);
    }

    [Fact]
    public void TracePivotTab_ShowsCorrelationIdPivot()
    {
        var msg = MakeMessage(correlationId: "corr-abc-123");

        var cut = RenderComponent<MessageDetailPane>(ps => ps
            .Add(p => p.Message, msg));

        Assert.Contains("corr-abc-123", cut.Markup);
        Assert.Contains("CorrelationId", cut.Markup);
    }

    [Fact]
    public void TracePivotTab_ShowsSessionIdPivot()
    {
        var msg = MakeMessage(sessionId: "sess-007");

        var cut = RenderComponent<MessageDetailPane>(ps => ps
            .Add(p => p.Message, msg));

        Assert.Contains("sess-007", cut.Markup);
    }

    [Fact]
    public void TracePivotTab_ShowsKnownAppPropertyPivot_OperationId()
    {
        var msg = MakeMessage();
        msg.ApplicationProperties["operation_Id"] = "op-xyz-789";

        var cut = RenderComponent<MessageDetailPane>(ps => ps
            .Add(p => p.Message, msg));

        Assert.Contains("op-xyz-789", cut.Markup);
        Assert.Contains("operation_Id", cut.Markup);
    }

    [Fact]
    public void TracePivotTab_ShowsEmptyMessage_WhenNoPivots()
    {
        // Message with no CorrelationId, no SessionId, no known app properties
        var msg = new SbMessage
        {
            MessageId = "bare-msg",
            Body = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow
        };

        var cut = RenderComponent<MessageDetailPane>(ps => ps
            .Add(p => p.Message, msg));

        Assert.Contains("No explicit trace identifiers", cut.Markup);
    }

    [Fact]
    public void TracePivotTab_ShowsExplicitReasonText()
    {
        var msg = MakeMessage(correlationId: "c-1");

        var cut = RenderComponent<MessageDetailPane>(ps => ps
            .Add(p => p.Message, msg));

        Assert.Contains("Trace pivots use explicit identifiers only", cut.Markup);
    }

    // ── Session filter CTA ─────────────────────────────────────────────────

    [Fact]
    public void FilterBySessionButton_NotRendered_WhenCallbackNotSet()
    {
        var msg = MakeMessage(sessionId: "sess-A");

        var cut = RenderComponent<MessageDetailPane>(ps => ps
            .Add(p => p.Message, msg));

        Assert.Empty(cut.FindAll("[data-testid='filter-by-session-btn']"));
    }

    [Fact]
    public void FilterBySessionButton_Rendered_WhenCallbackSet_AndSessionIdExists()
    {
        var msg = MakeMessage(sessionId: "sess-B");
        var captured = new List<string>();

        var cut = RenderComponent<MessageDetailPane>(ps => ps
            .Add(p => p.Message, msg)
            .Add(p => p.OnFilterBySession, EventCallback.Factory.Create<string>(this, s => captured.Add(s))));

        Assert.Single(cut.FindAll("[data-testid='filter-by-session-btn']"));
    }

    [Fact]
    public void FilterBySessionButton_InvokesCallback_WithSessionId()
    {
        var msg = MakeMessage(sessionId: "sess-C");
        var captured = new List<string>();

        var cut = RenderComponent<MessageDetailPane>(ps => ps
            .Add(p => p.Message, msg)
            .Add(p => p.OnFilterBySession, EventCallback.Factory.Create<string>(this, s => captured.Add(s))));

        cut.Find("[data-testid='filter-by-session-btn']").Click();

        Assert.Single(captured);
        Assert.Equal("sess-C", captured[0]);
    }

    [Fact]
    public void FilterBySessionButton_NotRendered_WhenSessionIdIsNull()
    {
        var msg = MakeMessage(sessionId: null);
        var captured = new List<string>();

        var cut = RenderComponent<MessageDetailPane>(ps => ps
            .Add(p => p.Message, msg)
            .Add(p => p.OnFilterBySession, EventCallback.Factory.Create<string>(this, s => captured.Add(s))));

        Assert.Empty(cut.FindAll("[data-testid='filter-by-session-btn']"));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static SbMessage MakeMessage(
        string messageId = "msg-001",
        string? correlationId = null,
        string? sessionId = null,
        string? deadLetterReason = null,
        string? deadLetterErrorDescription = null,
        string? partitionKey = null,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? lockedUntil = null,
        int deliveryCount = 1)
    {
        return new SbMessage
        {
            MessageId = messageId,
            Body = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow,
            CorrelationId = correlationId,
            SessionId = sessionId,
            DeadLetterReason = deadLetterReason,
            DeadLetterErrorDescription = deadLetterErrorDescription,
            DeliveryCount = deliveryCount,
            SystemProperties = new SbSystemProperties
            {
                PartitionKey = partitionKey,
                ExpiresAt = expiresAt,
                LockedUntil = lockedUntil
            }
        };
    }
}
