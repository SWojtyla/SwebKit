using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace SwebKit.E2E.Tests;

/// <summary>
/// End-to-end UI tests that connect to the running SwebKit MAUI app via WebView2 remote
/// debugging (CDP). The <see cref="AppFixture"/> launches the app (if needed), connects
/// Playwright, and exposes a ready <see cref="IPage"/> shared across all tests.
///
/// Prerequisites:
///   Build the app in Debug:
///     dotnet build src/SwebKit.App/SwebKit.App.csproj -c Debug -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
///
///   Run E2E tests (fixture auto-launches the app):
///     dotnet test tests/SwebKit.E2E.Tests --filter "Category=E2E"
///
///   Or pre-launch the app yourself (the fixture will detect it and skip launching):
///     Run SwebKit.App in Debug from Visual Studio / VS Code.
/// </summary>
[Trait("Category", "E2E")]
public sealed class AppUiTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;

    public AppUiTests(AppFixture fixture) => _fixture = fixture;

    // =========================================================================
    // AppShell_Renders — basic shell structure is present on startup
    // =========================================================================

    [Fact]
    public async Task AppShell_HasTopBar()
    {
        await Assertions.Expect(_fixture.Page.Locator(".top-bar"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    }

    [Fact]
    public async Task AppShell_HasAppLogo()
    {
        var logo = _fixture.Page.Locator(".app-logo-text");
        await Assertions.Expect(logo)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Assertions.Expect(logo).ToHaveTextAsync("SwebKit");
    }

    [Fact]
    public async Task AppShell_HasLeftNavigation()
    {
        await Assertions.Expect(_fixture.Page.Locator("nav.left-nav"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    }

    [Fact]
    public async Task AppShell_HasAllNavItems()
    {
        var areas = new[]
        {
            "dashboard", "service-bus", "aks", "redis",
            "storage", "pipelines", "observability", "settings"
        };

        foreach (var area in areas)
        {
            await Assertions.Expect(_fixture.Page.Locator($"[data-area=\"{area}\"]"))
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        }
    }

    [Fact]
    public async Task AppShell_HasCommandPaletteButton()
    {
        await Assertions.Expect(_fixture.Page.Locator(".cmd-palette-btn"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    }

    [Fact]
    public async Task AppShell_HasStatusBar()
    {
        await Assertions.Expect(_fixture.Page.Locator(".status-bar"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    }

    // =========================================================================
    // Navigation_Works — clicking nav items changes routes and active state
    // =========================================================================

    [Fact]
    public async Task Navigation_ToDashboard()
    {
        await NavigateToAsync("dashboard");

        await Assertions.Expect(_fixture.Page.Locator(".dashboard-page"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    }

    [Fact]
    public async Task Navigation_ToSettings()
    {
        await NavigateToAsync("settings");

        await Assertions.Expect(_fixture.Page.Locator(".settings-shell"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    }

    [Fact]
    public async Task Navigation_ToServiceBus()
    {
        await NavigateToAsync("dashboard");
        await Assertions.Expect(_fixture.Page.Locator(".dashboard-page"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        await NavigateToAsync("service-bus");

        await Assertions.Expect(_fixture.Page.Locator(".service-bus-page-shell"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    }

    [Fact]
    public async Task Navigation_NavToggle_CollapsesAndExpands()
    {
        var appShell = _fixture.Page.Locator(".app-shell");
        var navToggle = _fixture.Page.Locator(".nav-toggle");

        // Ensure we start in the expanded state regardless of prior test order.
        var isCollapsed = await appShell.EvaluateAsync<bool>(
            "el => el.classList.contains('nav-collapsed')");
        if (isCollapsed)
        {
            await navToggle.ClickAsync();
            await Assertions.Expect(appShell).Not.ToHaveClassAsync(
                new Regex(@"\bnav-collapsed\b"),
                new LocatorAssertionsToHaveClassOptions { Timeout = 5_000 });
        }

        // Collapse.
        await navToggle.ClickAsync();
        await Assertions.Expect(appShell).ToHaveClassAsync(
            new Regex(@"\bnav-collapsed\b"),
            new LocatorAssertionsToHaveClassOptions { Timeout = 5_000 });

        // Expand again.
        await navToggle.ClickAsync();
        await Assertions.Expect(appShell).Not.ToHaveClassAsync(
            new Regex(@"\bnav-collapsed\b"),
            new LocatorAssertionsToHaveClassOptions { Timeout = 5_000 });
    }

    // =========================================================================
    // DemoMode_Works — demo mode toggle shows/hides the banner
    // Each test enables and then disables demo so state is always clean.
    // =========================================================================

    [Fact]
    public async Task DemoMode_ToggleVisible()
    {
        await Assertions.Expect(_fixture.Page.Locator(".demo-toggle-btn"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    }

    [Fact]
    public async Task DemoMode_EnableShowsBanner()
    {
        await NavigateToAsync("dashboard");

        // Enable demo mode via the toggle + confirmation popover.
        await _fixture.Page.Locator(".demo-toggle-btn").ClickAsync();

        var confirmBtn = _fixture.Page.Locator(".demo-confirm-enable");
        await Assertions.Expect(confirmBtn)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await confirmBtn.ClickAsync();

        var banner = _fixture.Page.Locator(".demo-banner");
        await Assertions.Expect(banner)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Assertions.Expect(banner).ToContainTextAsync("DEMO MODE");

        // Clean up: disable demo so subsequent tests start from a clean state.
        await _fixture.Page.Locator("button.demo-banner-disable").ClickAsync();
        await Assertions.Expect(banner)
            .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 10_000 });
    }

    [Fact]
    public async Task DemoMode_DisableBanner()
    {
        await NavigateToAsync("dashboard");

        // Ensure demo mode is on so there is a banner to dismiss.
        await _fixture.Page.Locator(".demo-toggle-btn").ClickAsync();

        var confirmBtn = _fixture.Page.Locator(".demo-confirm-enable");
        await Assertions.Expect(confirmBtn)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await confirmBtn.ClickAsync();

        var banner = _fixture.Page.Locator(".demo-banner");
        await Assertions.Expect(banner)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // Disable demo via the banner's own dismiss button.
        await _fixture.Page.Locator("button.demo-banner-disable").ClickAsync();
        await Assertions.Expect(banner)
            .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 10_000 });
    }

    [Fact]
    public async Task ObservabilityLogs_GuidedFirstJourney_InDemoMode_RunsQuery()
    {
        await OpenObservabilityLogsInDemoModeAsync();

        var runButton = _fixture.Page.Locator("[data-testid=\"obs-run-query\"]");
        await runButton.ClickAsync();

        var resultsHeader = _fixture.Page.Locator(".obs-results-header");
        await Assertions.Expect(resultsHeader)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Assertions.Expect(resultsHeader).ToContainTextAsync("results");
    }

    [Fact]
    public async Task ObservabilityLogs_GuidedToAdvancedJourney_QueryStillRuns()
    {
        await OpenObservabilityLogsInDemoModeAsync();

        var advancedModeButton = _fixture.Page.Locator("[data-testid=\"obs-mode-advanced\"]");
        await advancedModeButton.ClickAsync();
        await Assertions.Expect(advancedModeButton).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(_fixture.Page.Locator(".obs-monaco-wrapper"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        var runButton = _fixture.Page.Locator("[data-testid=\"obs-run-query\"]");
        await runButton.ClickAsync();

        var resultsHeader = _fixture.Page.Locator(".obs-results-header");
        await Assertions.Expect(resultsHeader)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Assertions.Expect(resultsHeader).ToContainTextAsync("results");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task OpenObservabilityLogsInDemoModeAsync()
    {
        await NavigateToAsync("dashboard");
        await EnsureDemoModeEnabledAsync();

        await NavigateToAsync("observability");

        await Assertions.Expect(_fixture.Page.Locator(".obs-page"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var logsTab = _fixture.Page.Locator("[data-testid=\"obs-tab-logs\"]");
        await Assertions.Expect(logsTab)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await logsTab.ClickAsync();

        var runButton = _fixture.Page.Locator("[data-testid=\"obs-run-query\"]");
        await Assertions.Expect(runButton)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var guidedModeButton = _fixture.Page.Locator("[data-testid=\"obs-mode-guided\"]");
        await guidedModeButton.ClickAsync();

        await Assertions.Expect(_fixture.Page.Locator("[data-testid=\"obs-guided-builder\"]"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    }

    private async Task EnsureDemoModeEnabledAsync()
    {
        var banner = _fixture.Page.Locator(".demo-banner");
        if (await banner.IsVisibleAsync())
        {
            return;
        }

        var toggleButton = _fixture.Page.Locator(".demo-toggle-btn");
        await Assertions.Expect(toggleButton)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await toggleButton.ClickAsync();

        var confirmButton = _fixture.Page.Locator(".demo-confirm-enable");
        await Assertions.Expect(confirmButton)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await confirmButton.ClickAsync();

        await Assertions.Expect(banner)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    }

    /// <summary>Clicks the nav item for the given area to trigger navigation.</summary>
    private Task NavigateToAsync(string area) =>
        _fixture.Page.Locator($"[data-area=\"{area}\"]").ClickAsync();
}

