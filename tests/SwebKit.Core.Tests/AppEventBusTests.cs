using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public class AppEventBusTests
{
    private sealed record TestEventA(string Value);
    private sealed record TestEventB(int Value);

    private static AppEventBus CreateBus() =>
        new(NullLogger<AppEventBus>.Instance);

    private static AppEventBus CreateBus(ILogger<AppEventBus> logger) =>
        new(logger);

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

    [Fact]
    public async Task SubscribeAsync_InvokesAsyncHandler()
    {
        var bus = CreateBus();
        string? received = null;

        bus.Subscribe<TestEventA>(async e =>
        {
            await Task.Yield();
            received = e.Value;
        });

        await bus.PublishAsync(new TestEventA("async-ok"));

        Assert.Equal("async-ok", received);
    }

    [Fact]
    public async Task PublishAsync_SyncAndAsyncHandlers_BothFire()
    {
        var bus = CreateBus();
        var syncFired = false;
        var asyncFired = false;

        bus.Subscribe<TestEventA>(_ => syncFired = true);
        bus.Subscribe<TestEventA>(async _ =>
        {
            await Task.Yield();
            asyncFired = true;
        });

        await bus.PublishAsync(new TestEventA("both"));

        Assert.True(syncFired);
        Assert.True(asyncFired);
    }

    [Fact]
    public async Task PublishAsync_WhenAsyncHandlerThrows_OtherHandlersStillFire()
    {
        var bus = CreateBus();
        var secondFired = false;

        bus.Subscribe<TestEventA>(async _ =>
        {
            await Task.Yield();
            throw new InvalidOperationException("Intentional async test exception");
        });
        bus.Subscribe<TestEventA>(async _ =>
        {
            await Task.Yield();
            secondFired = true;
        });

        await bus.PublishAsync(new TestEventA("fault"));

        Assert.True(secondFired);
    }

    [Fact]
    public async Task UnsubscribeAsync_RemovesHandler()
    {
        var bus = CreateBus();
        var count = 0;
        Func<TestEventA, Task> handler = async _ =>
        {
            await Task.Yield();
            count++;
        };

        bus.Subscribe(handler);
        await bus.PublishAsync(new TestEventA("first"));
        bus.Unsubscribe(handler);
        await bus.PublishAsync(new TestEventA("second"));

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task PublishAsync_WhenNoSubscribers_DoesNotThrow()
    {
        var bus = CreateBus();

        var ex = await Record.ExceptionAsync(() => bus.PublishAsync(new TestEventA("orphan")));

        Assert.Null(ex);
    }

    [Fact]
    public void Publish_IgnoresAsyncHandlers()
    {
        var bus = CreateBus();
        var asyncCalled = false;

        bus.Subscribe<TestEventA>(async _ =>
        {
            await Task.Yield();
            asyncCalled = true;
        });

        bus.Publish(new TestEventA("sync-only"));

        Assert.False(asyncCalled);
    }

    [Fact]
    public void Publish_IgnoresAsyncHandlers_WithoutLoggingFalseErrors()
    {
        var logger = new TestLogger<AppEventBus>();
        var bus = CreateBus(logger);

        bus.Subscribe<TestEventA>(async _ =>
        {
            await Task.Yield();
        });

        bus.Publish(new TestEventA("sync-only"));

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void Publish_WhenSyncHandlerCancels_RethrowsOperationCanceledException()
    {
        var bus = CreateBus();
        Action<TestEventA> handler = _ => throw new OperationCanceledException();
        bus.Subscribe(handler);

        Assert.Throws<OperationCanceledException>(() => bus.Publish(new TestEventA("cancel")));
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
