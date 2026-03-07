using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public class AppEventBusTests
{
    private sealed record TestEventA(string Value);
    private sealed record TestEventB(int Value);

    [Fact]
    public void Publish_InvokesSubscriber()
    {
        var bus = new AppEventBus();
        string? received = null;

        bus.Subscribe<TestEventA>(e => received = e.Value);
        bus.Publish(new TestEventA("ok"));

        Assert.Equal("ok", received);
    }

    [Fact]
    public void Unsubscribe_CallbackIsNotInvoked()
    {
        var bus = new AppEventBus();
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
        var bus = new AppEventBus();
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
        var bus = new AppEventBus();
        var called = 0;

        bus.Subscribe<TestEventA>(_ => called++);
        bus.Publish(new TestEventB(42));

        Assert.Equal(0, called);
    }
}
