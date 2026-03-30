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
    {
        options ??= new CodeEmitOptions();

        var coalesced = CoalesceFills(actions);

        var sb = new StringBuilder();
        sb.Append(FrameworkTemplate.GetHeader(options));

        const string indent = "        ";

        for (var i = 0; i < coalesced.Count; i++)
        {
            if (options.PreserveTiming && i > 0)
            {
                var delay = coalesced[i].Source.Timestamp - coalesced[i - 1].Source.Timestamp;
                var ms = (int)delay.TotalMilliseconds;
                if (ms >= options.MinDelayMs)
                    sb.AppendLine($"{indent}await Task.Delay({ms});");
            }

            sb.AppendLine(ActionLineEmitter.Emit(coalesced[i], indent));
        }

        sb.Append(FrameworkTemplate.GetFooter(options));

        return sb.ToString();
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
