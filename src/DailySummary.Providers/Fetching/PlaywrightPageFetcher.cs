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

        // DOMContentLoaded, NOT NetworkIdle: ad/tracker-heavy news sites (MSN, investing.com, …)
        // never go network-idle, so NetworkIdle hangs until the nav timeout and starves the other
        // URLs in the section. DOMContentLoaded fires reliably; a short settle lets late content paint.
        try
        {
            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 30_000
            }).ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
            // Heavy/bot-hostile pages may still exceed the nav timeout — read whatever rendered.
        }

        await page.WaitForTimeoutAsync(1_500).ConfigureAwait(false);
        var title = await page.TitleAsync().ConfigureAwait(false);
        var text = await page.InnerTextAsync("body").ConfigureAwait(false);
        var links = await ExtractLinksAsync(page).ConfigureAwait(false);
        return new PageContent(url, title, text, links);
    }

    // Anchors are lost in innerText, so pull them separately as "headline\thref\tcontext" strings:
    // the anchor text (headline), the absolute href, and a short blurb from the nearest article/card
    // container (the dek/surrounding text) so the LLM knows what each link is about, not just its URL.
    private static async Task<IReadOnlyList<PageLink>> ExtractLinksAsync(IPage page)
    {
        try
        {
            var raw = await page.EvalOnSelectorAllAsync<string[]>("a[href]",
                @"els => els.map(e => {
                    const head = (e.textContent || '').trim().replace(/\s+/g, ' ');
                    const box = e.closest('article, li, section') || e.parentElement;
                    let ctx = box ? (box.textContent || '').trim().replace(/\s+/g, ' ') : '';
                    if (ctx.length > 200) ctx = ctx.slice(0, 200);
                    return head + '\t' + e.href + '\t' + ctx;
                  })
                  .filter(s => { const p = s.split('\t'); return p[0].length >= 25 && (p[1] || '').startsWith('http'); })
                  .slice(0, 80)").ConfigureAwait(false);

            var seen = new HashSet<string>();
            var links = new List<PageLink>();
            foreach (var s in raw)
            {
                var parts = s.Split('\t');
                if (parts.Length < 2) continue;
                var href = parts[1];
                var context = parts.Length > 2 ? parts[2] : string.Empty;
                if (seen.Add(href)) links.Add(new PageLink(parts[0], href, context));
            }
            return links;
        }
        catch (PlaywrightException)
        {
            return Array.Empty<PageLink>();
        }
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
            var engineName = _config.Engine.Equals("chromium", StringComparison.OrdinalIgnoreCase)
                ? "chromium" : "firefox";
            var engine = engineName == "chromium" ? _playwright.Chromium : _playwright.Firefox;

            try
            {
                _browser = await engine.LaunchAsync(launch).ConfigureAwait(false);
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist"))
            {
                // Browser binary not installed for this Playwright version — install it (no pwsh needed)
                // using the app's own version, then retry once. In Docker/CI the browser is baked in,
                // so this path never runs there.
                Microsoft.Playwright.Program.Main(new[] { "install", engineName });
                _browser = await engine.LaunchAsync(launch).ConfigureAwait(false);
            }
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
