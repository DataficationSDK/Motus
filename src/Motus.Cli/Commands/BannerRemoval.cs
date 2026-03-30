using Motus.Abstractions;

namespace Motus.Cli.Commands;

/// <summary>
/// Removes common cookie consent, privacy, and overlay banners from a page
/// before capture. Works by removing elements that match well-known selectors
/// and clearing body scroll locks that banners often apply.
/// </summary>
internal static class BannerRemoval
{
    private const string Script = """
        (() => {
            const selectors = [
                // Cookie / consent banners
                '[class*="cookie" i]',
                '[id*="cookie" i]',
                '[class*="consent" i]',
                '[id*="consent" i]',
                '[class*="gdpr" i]',
                '[id*="gdpr" i]',
                '[class*="cc-" i]',
                '[id*="cc-" i]',
                // Privacy / notice banners
                '[class*="privacy" i]',
                '[id*="privacy" i]',
                '[class*="notice-banner" i]',
                '[class*="cookie-banner" i]',
                '[class*="cookie-bar" i]',
                // Common third-party consent managers
                '#onetrust-banner-sdk',
                '#onetrust-consent-sdk',
                '.onetrust-pc-dark-filter',
                '#CybotCookiebotDialog',
                '#CybotCookiebotDialogBodyUnderlay',
                '#usercentrics-root',
                '.cc-window',
                '.cc-banner',
                '#cookieConsent',
                // Generic overlay / modal backdrops
                '[class*="overlay" i][class*="cookie" i]',
                '[class*="overlay" i][class*="consent" i]',
                '[class*="backdrop" i][class*="cookie" i]',
            ];

            const removed = new Set();
            for (const sel of selectors) {
                try {
                    for (const el of document.querySelectorAll(sel)) {
                        if (!removed.has(el)) {
                            el.remove();
                            removed.add(el);
                        }
                    }
                } catch { /* invalid selector on this page, skip */ }
            }

            // Clear scroll locks that banners commonly set
            document.body.style.overflow = '';
            document.body.style.position = '';
            document.documentElement.style.overflow = '';
            document.documentElement.style.position = '';
            document.body.classList.remove('no-scroll', 'modal-open', 'overflow-hidden');

            return removed.size;
        })()
        """;

    /// <summary>
    /// Evaluates the banner removal script on the page and returns the number
    /// of elements removed.
    /// </summary>
    public static async Task<int> RemoveAsync(IPage page)
    {
        return await page.EvaluateAsync<int>(Script);
    }
}
