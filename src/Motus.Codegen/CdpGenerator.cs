using System.Collections.Immutable;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Motus.Codegen.Emit;
using Motus.Codegen.Parser;

namespace Motus.Codegen;

/// <summary>
/// Roslyn incremental source generator that reads CDP protocol JSON
/// and emits typed C# domain classes.
/// </summary>
[Generator]
public sealed class CdpGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Collect the two CDP protocol JSON files by filename
        var browserJson = context.AdditionalTextsProvider
            .Where(static f => Path.GetFileName(f.Path) == "browser_protocol.json")
            .Select(static (f, ct) => f.GetText(ct)?.ToString())
            .Collect()
            .Select(static (arr, _) => arr.IsDefaultOrEmpty ? null : arr[0]);

        var jsJson = context.AdditionalTextsProvider
            .Where(static f => Path.GetFileName(f.Path) == "js_protocol.json")
            .Select(static (f, ct) => f.GetText(ct)?.ToString())
            .Collect()
            .Select(static (arr, _) => arr.IsDefaultOrEmpty ? null : arr[0]);

        // Combine both JSON sources into a single pair
        var combined = browserJson.Combine(jsJson);

        // Register source output; parsing happens here to avoid ImmutableArray equality issues
        context.RegisterSourceOutput(combined, static (spc, pair) =>
        {
            var (browserText, jsText) = pair;

            var allDomains = ImmutableArray.CreateBuilder<Model.CdpDomain>();

            if (browserText != null)
                allDomains.AddRange(CdpSchemaParser.Parse(browserText));

            if (jsText != null)
                allDomains.AddRange(CdpSchemaParser.Parse(jsText));

            if (allDomains.Count == 0)
                return;

            var domains = allDomains.ToImmutable();
            var resolver = new TypeResolver(domains);

            foreach (var domain in domains)
            {
                var source = DomainEmitter.Emit(domain, resolver);
                var hintName = $"Motus.Protocol.{domain.Name}Domain.g.cs";
                spc.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
            }
        });
    }
}
