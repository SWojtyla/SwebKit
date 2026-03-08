using Microsoft.Playwright;

namespace SwebKit.E2E.Tests;

/// <summary>
/// End-to-end UI tests that connect to a running SwebKit MAUI app via WebView2 remote debugging.
///
/// Prerequisites:
///   1. Set the environment variable before launching the app:
///        $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=9222"
///   2. Launch the MAUI app (dotnet run / F5 in VS).
///   3. Run these tests:
///        dotnet test tests/SwebKit.E2E.Tests --filter "FullyQualifiedName~AppUiTests"
/// </summary>
[Trait("Category", "E2E")]
public sealed class AppUiTests : IAsyncLifetime
{
    private const string CdpEndpoint = "http://localhost:9222";

    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IPage _page = null!;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.ConnectOverCDPAsync(CdpEndpoint);

        // WebView2 exposes the Blazor app in the first (default) browser context
        var context = _browser.Contexts[0];
        _page = context.Pages[0];
    }

    public async Task DisposeAsync()
    {
        // Don't close the page/context — we don't own the app process
        await _browser.CloseAsync();
        _playwright.Dispose();
    }

    [Fact]
    public async Task AppShell_Renders_TopBarWithLogo()
    {
        var logo = _page.Locator(".app-logo");
        await Assertions.Expect(logo).ToBeVisibleAsync();
        await Assertions.Expect(logo).ToHaveTextAsync("SwebKit");
    }

    [Fact]
    public async Task AppShell_Renders_LeftNavigation()
    {
        var nav = _page.Locator("nav.left-nav");
        await Assertions.Expect(nav).ToBeVisibleAsync();

        // Verify core navigation items are present
        await Assertions.Expect(_page.GetByText("Projects")).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByText("Service Bus")).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByText("Observability")).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByText("AKS")).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByText("Settings")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AppShell_Renders_CommandPaletteButton()
    {
        var button = _page.Locator(".cmd-palette-btn");
        await Assertions.Expect(button).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Navigation_CanSwitchToServiceBus()
    {
        // Click the Service Bus nav item
        await _page.GetByText("Service Bus").ClickAsync();

        // Wait for navigation to complete — the URL should contain /service-bus
        await _page.WaitForURLAsync("**/service-bus");
    }
}
