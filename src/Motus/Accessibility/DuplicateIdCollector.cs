namespace Motus;

/// <summary>
/// Pre-fetches the set of duplicate HTML id values in the document.
///
/// Uses Runtime.evaluate with a JS snippet rather than DOM.getDocument + DOM.querySelectorAll
/// CDP commands, which reduces the interaction to a single CDP round-trip rather than three
/// or more (getDocument, querySelectorAll, then getAttributes per matched node).
///
/// Risk: If page-level JS execution is restricted (e.g., certain CSP configurations), this
/// will silently return no duplicates. The DOM domain commands operate at the protocol level
/// and are unaffected by page JS restrictions. In practice the risk is low because Motus
/// controls the browser instance and JS execution is always available.
/// </summary>
internal static class DuplicateIdCollector
{
    internal static async Task<IReadOnlySet<string>> CollectAsync(
        IMotusSession session, CancellationToken ct)
    {
        try
        {
            // Use Runtime.evaluate for a simpler, single-call approach
            var result = await session.SendAsync(
                "Runtime.evaluate",
                new RuntimeEvaluateParams(
                    """
                    (() => {
                        const ids = {};
                        for (const el of document.querySelectorAll('[id]')) {
                            const id = el.id;
                            if (id) ids[id] = (ids[id] || 0) + 1;
                        }
                        return JSON.stringify(Object.keys(ids).filter(k => ids[k] > 1));
                    })()
                    """,
                    ReturnByValue: true),
                CdpJsonContext.Default.RuntimeEvaluateParams,
                CdpJsonContext.Default.RuntimeEvaluateResult,
                ct).ConfigureAwait(false);

            var json = result.Result.Value?.ToString();
            if (string.IsNullOrEmpty(json))
                return new HashSet<string>();

            var duplicates = System.Text.Json.JsonSerializer.Deserialize<string[]>(json);
            return duplicates is null
                ? new HashSet<string>()
                : new HashSet<string>(duplicates, StringComparer.Ordinal);
        }
        catch
        {
            return new HashSet<string>();
        }
    }
}
