namespace Motus;

/// <summary>
/// Pre-fetches the document's lang attribute via Runtime.evaluate.
/// </summary>
internal static class DocumentLanguageCollector
{
    internal static async Task<string?> CollectAsync(IMotusSession session, CancellationToken ct)
    {
        try
        {
            var result = await session.SendAsync(
                "Runtime.evaluate",
                new RuntimeEvaluateParams("document.documentElement.lang || ''", ReturnByValue: true),
                CdpJsonContext.Default.RuntimeEvaluateParams,
                CdpJsonContext.Default.RuntimeEvaluateResult,
                ct).ConfigureAwait(false);

            var lang = result.Result.Value?.ToString();
            return string.IsNullOrWhiteSpace(lang) ? null : lang;
        }
        catch
        {
            return null;
        }
    }
}
