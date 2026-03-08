using Microsoft.Playwright;

namespace SwebKit.E2E.Tests;

/// <summary>
/// Playwright smoke tests verifying the E2E infrastructure is correctly set up.
/// These tests do not require the MAUI app to be running.
/// Run `dotnet test tests/SwebKit.E2E.Tests` after ensuring browsers are installed:
///   pwsh tests/SwebKit.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
/// </summary>
public sealed class PlaywrightSmokeTests : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    [Fact]
    public async Task Playwright_CanLaunchBrowser_AndNavigateToBlankPage()
    {
        var page = await _browser.NewPageAsync();

        await page.GotoAsync("about:blank");

        Assert.Equal("", await page.TitleAsync());
    }

    [Fact]
    public async Task Playwright_CanEvaluateJavaScript()
    {
        var page = await _browser.NewPageAsync();
        await page.GotoAsync("about:blank");

        var result = await page.EvaluateAsync<int>("() => 2 + 2");

        Assert.Equal(4, result);
    }

    [Fact]
    public async Task Playwright_CanRenderInlineHtml_AndFindElements()
    {
        var page = await _browser.NewPageAsync();

        await page.SetContentAsync("""
            <!DOCTYPE html>
            <html>
              <body>
                <h1 id="title">SwebKit E2E</h1>
                <button id="btn">Click me</button>
              </body>
            </html>
            """);

        var heading = page.Locator("#title");
        Assert.Equal("SwebKit E2E", await heading.TextContentAsync());

        var button = page.Locator("#btn");
        Assert.Equal("Click me", await button.TextContentAsync());
    }
}
