using Microsoft.Playwright;

namespace Playwright.NSE.Indices.Browser;

public record BrowserAndContext(IBrowser Browser, IBrowserContext Context);

public static class BrowserFactory
{
    /// <summary>
    /// Launches a real Edge browser with anti-bot flags masked,
    /// creates a context that accepts downloads, and returns the context.
    /// Caller is responsible for disposing browser and context.
    /// </summary>
    public static async Task<BrowserAndContext> CreateAsync(IPlaywright playwright)
    {
        var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = false,
            Channel  = "msedge",
            Args     = new[] { "--disable-blink-features=AutomationControlled" }
        });

        var context = await browser.NewContextAsync(new() { AcceptDownloads = true });

        // Mask navigator.webdriver so the site treats us as a human user
        await context.AddInitScriptAsync(
            "Object.defineProperty(navigator, 'webdriver', { get: () => undefined });");

        return new BrowserAndContext(browser, context);
    }

    /// <summary>
    /// Blocks images, media, and fonts on the given page to speed up initial load.
    /// Call once immediately after page creation, before GotoAsync.
    /// </summary>
    public static async Task BlockHeavyAssetsAsync(IPage page)
    {
        await page.RouteAsync("**/*", route =>
        {
            var type = route.Request.ResourceType;
            if (type == "image" || type == "media" || type == "font")
                return route.AbortAsync();
            return route.ContinueAsync();
        });
    }
}
