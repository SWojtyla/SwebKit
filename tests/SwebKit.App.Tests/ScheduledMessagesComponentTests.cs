using Bunit;
using Bunit.JSInterop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Components.ServiceBus;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.App.Tests;

[Collection("AppDataSerial")]
public sealed class ScheduledMessagesComponentTests : TestContext
{
    public ScheduledMessagesComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);

        Services.AddFluentUIComponents();
    }

    [Fact]
    public async Task NoEntries_ShowsEmptyState()
    {
        using var sandbox = new AppDataSandbox();
        var repo = new ScheduledMessageRepository();
        Services.AddSingleton(repo);

        var cut = RenderComponent<ScheduledMessages>(ps => ps
            .Add(p => p.NamespaceId, Guid.NewGuid()));

        Assert.Contains("No scheduled messages", cut.Markup);
    }

    [Fact]
    public async Task FutureEntry_ShowsPendingStatus_AndCancelButton()
    {
        using var sandbox = new AppDataSandbox();
        var ns = Guid.NewGuid();
        var repo = new ScheduledMessageRepository();
        await repo.AddAsync(new ScheduledMessageEntry
        {
            NamespaceId = ns,
            EntityPath = "orders",
            SequenceNumber = 12345,
            ScheduledEnqueueTime = DateTimeOffset.UtcNow.AddHours(2),
            MessageId = "msg-future",
            Subject = "future-subject"
        });
        Services.AddSingleton(repo);

        var cut = RenderComponent<ScheduledMessages>(ps => ps
            .Add(p => p.NamespaceId, ns));

        Assert.Contains("Pending", cut.Markup);
        Assert.Contains("Cancel", cut.Markup);
        Assert.Contains("12345", cut.Markup);
    }

    [Fact]
    public async Task PastEntry_ShowsEnqueuedStatus_NoCancelButton()
    {
        using var sandbox = new AppDataSandbox();
        var ns = Guid.NewGuid();
        var repo = new ScheduledMessageRepository();
        await repo.AddAsync(new ScheduledMessageEntry
        {
            NamespaceId = ns,
            EntityPath = "orders",
            SequenceNumber = 99999,
            ScheduledEnqueueTime = DateTimeOffset.UtcNow.AddHours(-1),
            MessageId = "msg-past"
        });
        Services.AddSingleton(repo);

        var cut = RenderComponent<ScheduledMessages>(ps => ps
            .Add(p => p.NamespaceId, ns));

        Assert.Contains("Enqueued", cut.Markup);
        Assert.DoesNotContain(">Cancel<", cut.Markup);
    }

    [Fact]
    public async Task EntityPathFilter_ShowsOnlyMatchingEntity()
    {
        using var sandbox = new AppDataSandbox();
        var ns = Guid.NewGuid();
        var repo = new ScheduledMessageRepository();
        await repo.AddAsync(new ScheduledMessageEntry
        {
            NamespaceId = ns,
            EntityPath = "orders",
            SequenceNumber = 111,
            ScheduledEnqueueTime = DateTimeOffset.UtcNow.AddHours(1)
        });
        await repo.AddAsync(new ScheduledMessageEntry
        {
            NamespaceId = ns,
            EntityPath = "payments",
            SequenceNumber = 222,
            ScheduledEnqueueTime = DateTimeOffset.UtcNow.AddHours(1)
        });
        Services.AddSingleton(repo);

        var cut = RenderComponent<ScheduledMessages>(ps => ps
            .Add(p => p.NamespaceId, ns)
            .Add(p => p.EntityPath, "orders"));

        Assert.Contains("111", cut.Markup);
        Assert.DoesNotContain("222", cut.Markup);
    }

    [Fact]
    public async Task RemoveButton_AlwaysPresent_RemovesEntryLocally()
    {
        using var sandbox = new AppDataSandbox();
        var ns = Guid.NewGuid();
        var repo = new ScheduledMessageRepository();
        await repo.AddAsync(new ScheduledMessageEntry
        {
            NamespaceId = ns,
            EntityPath = "orders",
            SequenceNumber = 55555,
            ScheduledEnqueueTime = DateTimeOffset.UtcNow.AddHours(3),
            MessageId = "msg-remove"
        });
        Services.AddSingleton(repo);

        var cut = RenderComponent<ScheduledMessages>(ps => ps
            .Add(p => p.NamespaceId, ns));

        Assert.Contains("55555", cut.Markup);

        var removeBtn = cut.FindAll("button").First(b => b.TextContent.Trim() == "Remove");
        removeBtn.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No scheduled messages", cut.Markup);
        });
    }

    private sealed class AppDataSandbox : IDisposable
    {
        private const string AppDataRootOverrideVariable = "SWEBKIT_APPDATA_ROOT";

        private readonly string? _originalRoot;
        private readonly string _tempRoot;

        public AppDataSandbox()
        {
            _originalRoot = Environment.GetEnvironmentVariable(AppDataRootOverrideVariable);
            _tempRoot = Path.Combine(Path.GetTempPath(), "SwebKit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            Environment.SetEnvironmentVariable(AppDataRootOverrideVariable, _tempRoot);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(AppDataRootOverrideVariable, _originalRoot);
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
