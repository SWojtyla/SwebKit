using Microsoft.Playwright;
using System.Diagnostics;

namespace SwebKit.E2E.Tests;

/// <summary>
/// Shared xunit fixture that connects Playwright to the running SwebKit MAUI app via
/// WebView2 remote debugging (CDP). If the app is not already running it will be
/// launched automatically from the Debug build output.
///
/// Lifetime: one fixture instance is shared across all tests in a class that implements
/// IClassFixture&lt;AppFixture&gt;. The CDP browser connection and Playwright instance are
/// created once and reused.
/// </summary>
public sealed class AppFixture : IAsyncLifetime
{
    public const int CdpPort = 9222;
    public const string CdpEndpoint = "http://localhost:9222";
    private const int ShellTimeoutMs = 30_000;

    /// <summary>The Playwright page connected to the running MAUI/WebView2 app.</summary>
    public IPage Page { get; private set; } = null!;

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private Process? _appProcess;

    public async Task InitializeAsync()
    {
        if (!await IsCdpListeningAsync())
        {
            _appProcess = LaunchApp();
            await WaitForCdpAsync();
        }

        await ConnectAsync();
    }

    public async Task ResetShellStateAsync()
    {
        await WaitForShellAsync();

        var disableDemoButton = Page.Locator("button.demo-banner-disable");
        if (await disableDemoButton.IsVisibleAsync())
        {
            await disableDemoButton.ClickAsync();
            await Assertions.Expect(disableDemoButton)
                .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 10_000 });
            await WaitForShellAsync();
        }

        await Page.EvaluateAsync(
            """
            () => {
                localStorage.setItem('swebkit-ui-theme', 'dark');
            }
            """);

        await HardNavigateAsync("/");
        await Page.ReloadAsync();
        await WaitForShellAsync();

        await Assertions.Expect(Page.Locator(".dashboard-page"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Assertions.Expect(Page.Locator(".demo-toggle-btn"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Assertions.Expect(Page.Locator(".app-shell"))
            .ToHaveAttributeAsync("data-theme", "dark");
    }

    public async Task HardNavigateAsync(string relativePath)
    {
        var targetUri = new Uri(new Uri(Page.Url), relativePath);

        await Page.EvaluateAsync(
            """
            path => {
                // Drive Blazor's client-side route pipeline directly. In the WebView2/CDP
                // test harness, forcing document navigation can load the right page markup
                // without refreshing shell route state the same way a routed transition does.
                const target = new URL(path, window.location.href);
                const next = `${target.pathname}${target.search}${target.hash}`;
                history.replaceState(history.state, '', next);
                dispatchEvent(new PopStateEvent('popstate', { state: history.state }));
            }
            """,
            relativePath);

        if (!UrlMatches(Page.Url, targetUri))
        {
            await Page.WaitForURLAsync(
                url => UrlMatches(url, targetUri),
                new PageWaitForURLOptions { Timeout = ShellTimeoutMs });
        }

        await WaitForShellAsync();
    }

    public async Task ReloadAsync()
    {
        await Page.ReloadAsync();
        await WaitForShellAsync();
    }

    public async Task ReconnectAsync()
    {
        await DisconnectAsync();
        await ConnectAsync();
    }

    public async Task DisposeAsync()
    {
        await DisconnectAsync();

        if (_appProcess is not null)
        {
            try { _appProcess.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            _appProcess.Dispose();
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task ConnectAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.ConnectOverCDPAsync(CdpEndpoint);

        var context = _browser.Contexts.Count > 0
            ? _browser.Contexts[0]
            : throw new InvalidOperationException(
                "CDP browser has no contexts. Ensure the app is running and WebView2 has initialised.");

        Page = await WaitForFirstPageAsync(context);
        await WaitForShellAsync();
    }

    private async Task DisconnectAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
            _browser = null;
        }

        if (_playwright is not null)
        {
            _playwright.Dispose();
            _playwright = null;
        }
    }

    private async Task WaitForShellAsync()
    {
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded,
            new PageWaitForLoadStateOptions { Timeout = ShellTimeoutMs });
        await Assertions.Expect(Page.Locator(".app-shell"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = ShellTimeoutMs });
    }

    private static async Task<bool> IsCdpListeningAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        try
        {
            var response = await client.GetAsync($"{CdpEndpoint}/json/version");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static Process LaunchApp()
    {
        var exePath = ResolveAppExePath();
        var psi = new ProcessStartInfo(exePath) { UseShellExecute = false };
        psi.EnvironmentVariables["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"] =
            $"--remote-debugging-port={CdpPort}";

        return Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {exePath}");
    }

    private static string ResolveAppExePath()
    {
        var overrideExe = Environment.GetEnvironmentVariable("SWEBKIT_APP_EXE");
        if (!string.IsNullOrEmpty(overrideExe))
        {
            if (!File.Exists(overrideExe))
                throw new FileNotFoundException(
                    $"SWEBKIT_APP_EXE points to a file that does not exist: {overrideExe}");
            return overrideExe;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any() || dir.EnumerateFiles("*.sln").Any())
                break;
            dir = dir.Parent;
        }

        if (dir is null)
            throw new DirectoryNotFoundException(
                "Could not find the solution root by walking up from the test output directory. " +
                "Set SWEBKIT_APP_EXE to the full path of SwebKit.App.exe.");

        var outputRoot = Path.Combine(
            dir.FullName,
            "src", "SwebKit.App", "bin", "Debug",
            "net10.0-windows10.0.19041.0");

        var candidates = new[]
        {
            Path.Combine(outputRoot, "SwebKit.App.exe"),
            Path.Combine(outputRoot, "win-x64", "SwebKit.App.exe"),
            Path.Combine(outputRoot, "win-arm64", "SwebKit.App.exe"),
        };

        var exePath = candidates.FirstOrDefault(File.Exists);

        if (exePath is null)
            throw new FileNotFoundException(
                $"App executable not found. Tried:\n  {string.Join("\n  ", candidates)}\n" +
                "Build the app first:\n" +
                "  dotnet build src/SwebKit.App/SwebKit.App.csproj -c Debug -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None");

        return exePath;
    }

    private static async Task WaitForCdpAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < 30)
        {
            try
            {
                var response = await client.GetAsync($"{CdpEndpoint}/json/version");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch { /* not ready yet */ }

            await Task.Delay(500);
        }

        throw new TimeoutException(
            $"CDP did not become available at {CdpEndpoint} within 30 seconds.");
    }

    private static async Task<IPage> WaitForFirstPageAsync(IBrowserContext context)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < 30)
        {
            if (context.Pages.Count > 0)
                return context.Pages[0];

            await Task.Delay(500);
        }

        throw new TimeoutException(
            "No pages appeared in the CDP browser context within 30 seconds.");
    }

    private static bool UrlMatches(string currentUrl, Uri targetUri)
    {
        if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out var currentUri))
        {
            return false;
        }

        return string.Equals(currentUri.AbsolutePath, targetUri.AbsolutePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(currentUri.Query, targetUri.Query, StringComparison.Ordinal)
            && string.Equals(currentUri.Fragment, targetUri.Fragment, StringComparison.Ordinal);
    }
}
