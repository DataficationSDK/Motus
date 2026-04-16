using System.Text;
using Motus.Recorder.Records;

namespace Motus.Recorder.CodeEmit;

/// <summary>
/// Converts a sequence of <see cref="ResolvedAction"/> into compilable C# test code.
/// </summary>
public sealed class CodeEmitter
{
    /// <summary>
    /// Emits a complete C# test file from the given recorded actions.
    /// </summary>
    public string Emit(IReadOnlyList<ResolvedAction> actions, CodeEmitOptions? options = null)
        => EmitWithMetadata(actions, options).Source;

    /// <summary>
    /// Emits a complete C# test file and returns per-locator metadata (selector, method,
    /// source line, page URL, backend node id) alongside the source.
    /// </summary>
    public EmitResult EmitWithMetadata(IReadOnlyList<ResolvedAction> actions, CodeEmitOptions? options = null)
    {
        options ??= new CodeEmitOptions();

        var coalesced = CoalesceFills(actions);

        var sb = new StringBuilder();
        var locators = new List<EmittedLocator>();

        var header = FrameworkTemplate.GetHeader(options);
        sb.Append(header);
        var currentLine = CountLines(header);

        const string indent = "        ";

        for (var i = 0; i < coalesced.Count; i++)
        {
            if (options.PreserveTiming && i > 0)
            {
                var delay = coalesced[i].Source.Timestamp - coalesced[i - 1].Source.Timestamp;
                var ms = (int)delay.TotalMilliseconds;
                if (ms >= options.MinDelayMs)
                {
                    sb.AppendLine($"{indent}await Task.Delay({ms});");
                    currentLine++;
                }
            }

            var resolved = coalesced[i];
            var line = ActionLineEmitter.Emit(resolved, indent);
            sb.AppendLine(line);
            currentLine++;

            if (resolved.Selector is not null && resolved.LocatorMethod is not null)
            {
                // Action lines with a selector emit exactly one locator call on `currentLine`.
                locators.Add(new EmittedLocator(
                    Selector: resolved.Selector,
                    LocatorMethod: resolved.LocatorMethod,
                    SourceLine: currentLine,
                    PageUrl: resolved.Source.PageUrl,
                    BackendNodeId: resolved.BackendNodeId));
            }

            // Scroll actions can emit two physical lines joined with \n inside a single Emit call.
            currentLine += CountExtraNewlines(line);
        }

        sb.Append(FrameworkTemplate.GetFooter(options));

        return new EmitResult(sb.ToString(), locators);
    }

    private static int CountLines(string text)
    {
        if (text.Length == 0)
            return 0;
        var count = 0;
        foreach (var c in text)
        {
            if (c == '\n')
                count++;
        }
        return count;
    }

    private static int CountExtraNewlines(string line)
    {
        // AppendLine added one trailing newline; count any embedded newlines beyond that.
        var count = 0;
        foreach (var c in line)
        {
            if (c == '\n')
                count++;
        }
        return count;
    }

    /// <summary>
    /// Merges consecutive <see cref="FillAction"/> records that target the same
    /// selector into a single entry with the last value. This handles cases where
    /// the debounce timer fires between keystrokes due to CDP latency.
    /// </summary>
    private static IReadOnlyList<ResolvedAction> CoalesceFills(IReadOnlyList<ResolvedAction> actions)
    {
        if (actions.Count <= 1)
            return actions;

        var result = new List<ResolvedAction>(actions.Count);

        for (var i = 0; i < actions.Count; i++)
        {
            var current = actions[i];

            if (current.Source is FillAction && current.Selector is not null)
            {
                // Look ahead and skip consecutive fills on the same selector
                while (i + 1 < actions.Count
                       && actions[i + 1].Source is FillAction
                       && actions[i + 1].Selector == current.Selector)
                {
                    i++;
                    current = actions[i];
                }
            }

            result.Add(current);
        }

        return result;
    }
}
