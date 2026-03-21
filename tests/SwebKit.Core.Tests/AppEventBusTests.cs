using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public class AppEventBusTests
{
    private sealed record TestEventA(string Value);
    private sealed record TestEventB(int Value);

    private static AppEventBus CreateBus() =>
        new(NullLogger<AppEventBus>.Instance);

    [Fact]
    public void Publish_InvokesSubscriber()
    {
        var bus = CreateBus();
        string? received = null;

        bus.Subscribe<TestEventA>(e => received = e.Value);
        bus.Publish(new TestEventA("ok"));

        Assert.Equal("ok", received);
    }

    [Fact]
    public void Unsubscribe_CallbackIsNotInvoked()
    {
        var bus = CreateBus();
        var called = 0;
        void Handler(TestEventA _) => called++;

        bus.Subscribe<TestEventA>(Handler);
        bus.Unsubscribe<TestEventA>(Handler);
        bus.Publish(new TestEventA("ignored"));

        Assert.Equal(0, called);
    }

    [Fact]
    public void MultipleSubscribers_AllInvoked()
    {
        var bus = CreateBus();
        var calledA = 0;
        var calledB = 0;

        bus.Subscribe<TestEventA>(_ => calledA++);
        bus.Subscribe<TestEventA>(_ => calledB++);
        bus.Publish(new TestEventA("x"));

        Assert.Equal(1, calledA);
        Assert.Equal(1, calledB);
    }

    [Fact]
    public void DifferentEventTypes_NoLeakage()
    {
        var bus = CreateBus();
        var called = 0;

        bus.Subscribe<TestEventA>(_ => called++);
        bus.Publish(new TestEventB(42));

        Assert.Equal(0, called);
    }

    [Fact]
    public void Publish_WhenOneSubscriberThrows_OtherSubscribersStillFire()
    {
        // AppEventBus catches and logs subscriber exceptions, so both behaviours apply:
        // the publish call must not throw, and the second subscriber must fire.
        var bus = CreateBus();
        var secondHandlerFired = false;

        bus.Subscribe<TestEventA>(_ => throw new InvalidOperationException("Intentional test exception"));
        bus.Subscribe<TestEventA>(_ => secondHandlerFired = true);

        var ex = Record.Exception(() => bus.Publish(new TestEventA("test-event")));

        Assert.Null(ex);
        Assert.True(secondHandlerFired);
    }

    [Fact]
    public void Publish_WhenNoSubscribers_DoesNotThrow()
    {
        var bus = CreateBus();

        var ex = Record.Exception(() => bus.Publish(new TestEventA("orphan-event")));

        Assert.Null(ex);
    }

    [Fact]
    public void Unsubscribe_RemovesHandler()
    {
        var bus = CreateBus();
        var count = 0;
        Action<TestEventA> handler = _ => count++;

        bus.Subscribe(handler);
        bus.Publish(new TestEventA("first"));
        bus.Unsubscribe(handler);
        bus.Publish(new TestEventA("second"));

        Assert.Equal(1, count);
    }
}
