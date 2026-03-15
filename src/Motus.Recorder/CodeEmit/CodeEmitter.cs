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

        var sb = new StringBuilder();
        sb.Append(FrameworkTemplate.GetHeader(options));

        const string indent = "        ";

        for (var i = 0; i < actions.Count; i++)
        {
            if (options.PreserveTiming && i > 0)
            {
                var delay = actions[i].Source.Timestamp - actions[i - 1].Source.Timestamp;
                var ms = (int)delay.TotalMilliseconds;
                if (ms >= options.MinDelayMs)
                    sb.AppendLine($"{indent}await Task.Delay({ms});");
            }

            sb.AppendLine(ActionLineEmitter.Emit(actions[i], indent));
        }

        sb.Append(FrameworkTemplate.GetFooter(options));

        return sb.ToString();
    }
}
