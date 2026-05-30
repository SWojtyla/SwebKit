using Bunit;
using Bunit.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Components.ServiceBus;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.App.Tests;

[Collection("AppDataSerial")]
public sealed class TemplatePickerTests : TestContext
{
    public TemplatePickerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);

        Services.AddFluentUIComponents();
        Services.AddSingleton(new AppStateService(new ProfileRepository(), new UiStateRepository(), new AppEventBus(NullLogger<AppEventBus>.Instance)));
    }

    [Fact]
    public async Task SearchFiltersTemplates_AndApplySelectsMatch()
    {
        using var sandbox = new AppDataSandbox();

        var appState = Services.GetRequiredService<AppStateService>();
        await appState.SaveMessageTemplateAsync(new SbMessageTemplate
        {
            Name = "Order Template",
            Body = "{}",
            Subject = "order.created"
        });
        await appState.SaveMessageTemplateAsync(new SbMessageTemplate
        {
            Name = "Payment Template",
            Body = "{}",
            Subject = "payment.received"
        });

        SbMessageTemplate? selected = null;
        var cut = RenderComponent<TemplatePicker>(ps => ps
            .Add(p => p.OnTemplateSelected, EventCallback.Factory.Create<SbMessageTemplate>(this, t => selected = t)));

        cut.Find("[data-testid='template-search-input']").Input("pay");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Payment Template", cut.Markup);
            Assert.DoesNotContain("Order Template", cut.Markup);
            Assert.Contains("1 match(es)", cut.Markup);
        });

        cut.Find("[data-testid='apply-template-button']").Click();

        Assert.NotNull(selected);
        Assert.Equal("Payment Template", selected!.Name);
    }

    [Fact]
    public async Task RenameBlank_ShowsValidation_AndLeavesNameUnchanged()
    {
        using var sandbox = new AppDataSandbox();

        var appState = Services.GetRequiredService<AppStateService>();
        await appState.SaveMessageTemplateAsync(new SbMessageTemplate
        {
            Name = "Original Template",
            Body = "{}"
        });

        var cut = RenderComponent<TemplatePicker>();

        cut.Find("[data-testid='rename-template-button']").Click();
        cut.Find("[data-testid='rename-template-input']").Change("   ");
        cut.Find("[data-testid='confirm-rename-button']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Template name is required.", cut.Markup));
        Assert.Equal("Original Template", Assert.Single(appState.MessageTemplates).Name);
    }

    [Fact]
    public async Task EditAndDeleteTemplate_UpdatesRepositoryState()
    {
        using var sandbox = new AppDataSandbox();

        var appState = Services.GetRequiredService<AppStateService>();
        await appState.SaveMessageTemplateAsync(new SbMessageTemplate
        {
            Name = "Editable Template",
            Body = "{\"before\":true}"
        });

        var cut = RenderComponent<TemplatePicker>();

        cut.Find("[data-testid='edit-template-button']").Click();
        cut.Find("[data-testid='edit-template-body']").Change("{\"after\":true}");
        cut.Find("[data-testid='save-edit-template-button']").Click();

        cut.WaitForAssertion(() =>
            Assert.Equal("{\"after\":true}", Assert.Single(appState.MessageTemplates).Body));

        cut.Find("[data-testid='delete-template-button']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(appState.MessageTemplates);
            Assert.Contains("No templates saved yet.", cut.Markup);
        });
    }

    private sealed class AppDataSandbox : IDisposable
    {
        private readonly string? _originalAppData;
        private readonly string? _originalRootOverride;
        private readonly string _tempRoot;

        public AppDataSandbox()
        {
            _originalAppData = Environment.GetEnvironmentVariable("APPDATA");
            _originalRootOverride = Environment.GetEnvironmentVariable("SWEBKIT_APPDATA_ROOT");
            _tempRoot = Path.Combine(Path.GetTempPath(), "SwebKit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            Environment.SetEnvironmentVariable("APPDATA", _tempRoot);
            Environment.SetEnvironmentVariable("SWEBKIT_APPDATA_ROOT", _tempRoot);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("APPDATA", _originalAppData);
            Environment.SetEnvironmentVariable("SWEBKIT_APPDATA_ROOT", _originalRootOverride);
            DeleteTempRoot();
        }

        private void DeleteTempRoot()
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Directory.Exists(_tempRoot))
                    {
                        Directory.Delete(_tempRoot, recursive: true);
                    }

                    return;
                }
                catch (IOException) when (attempt < 4)
                {
                    Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException) when (attempt < 4)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}
