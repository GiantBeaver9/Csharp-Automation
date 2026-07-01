using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;
using Microsoft.Playwright;

namespace DailySummary.Providers.Fetching;

/// <summary>
/// Captures fully-rendered pages with Playwright. Firefox headed by default (evades bot detection
/// better than headless Chromium). One browser per instance; a fresh context per fetch (cheap isolation).
/// </summary>
public sealed class PlaywrightPageFetcher : IPageFetcher
{
    private readonly BrowserConfig _config;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightPageFetcher(BrowserConfig config) => _config = config;

    public async Task<PageContent> FetchAsync(string url, CancellationToken ct)
    {
        var browser = await EnsureBrowserAsync().ConfigureAwait(false);
        await using var context = await browser.NewContextAsync().ConfigureAwait(false);
        var page = await context.NewPageAsync().ConfigureAwait(false);

        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle }).ConfigureAwait(false);
        var title = await page.TitleAsync().ConfigureAwait(false);
        var text = await page.InnerTextAsync("body").ConfigureAwait(false);
        return new PageContent(url, title, text);
    }

    private async Task<IBrowser> EnsureBrowserAsync()
    {
        if (_browser is not null) return _browser;
        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_browser is not null) return _browser;
            _playwright = await Playwright.CreateAsync().ConfigureAwait(false);
            var launch = new BrowserTypeLaunchOptions { Headless = !_config.Headed };
            var engine = _config.Engine.Equals("chromium", StringComparison.OrdinalIgnoreCase)
                ? _playwright.Chromium
                : _playwright.Firefox;
            _browser = await engine.LaunchAsync(launch).ConfigureAwait(false);
            return _browser;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync().ConfigureAwait(false);
        _playwright?.Dispose();
        _initLock.Dispose();
    }
}
