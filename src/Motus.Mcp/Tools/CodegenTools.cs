using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Motus.Recorder.PageAnalysis;
using Motus.Recorder.PomEmit;

namespace Motus.Mcp;

/// <summary>
/// Generates test scaffolding from the active page. Analyzes the live DOM,
/// infers a stable locator for each interactable element, and emits a Page
/// Object Model class the agent can drop into a test project.
/// </summary>
[McpServerToolType]
public sealed class CodegenTools
{
    [McpServerTool(Name = "generate_pom", Title = "Generate page object model", Destructive = false, ReadOnly = true)]
    [Description("Analyzes the active page and returns a C# Page Object Model class for it: a property "
        + "with an inferred locator for each interactable element, plus a constructor and a navigate "
        + "helper. The generated source is returned inline.")]
    public static async Task<CallToolResult> GeneratePomAsync(
        [Description("The namespace for the generated class. Defaults to Motus.Generated.")] string? @namespace,
        [Description("The name of the generated class. Defaults to a name derived from the page URL.")] string? class_name,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            var url = page.Url;

            var engine = PageAnalysisEngine.Create(page);
            var elements = await engine.AnalyzeAsync(page, cancellationToken).ConfigureAwait(false);

            var className = !string.IsNullOrWhiteSpace(class_name)
                ? class_name
                : PageClassNameDeriver.Derive(url ?? "");

            var source = new PomEmitter().Emit(elements, new PomEmitOptions
            {
                Namespace = !string.IsNullOrWhiteSpace(@namespace) ? @namespace : "Motus.Generated",
                ClassName = className,
                PageUrl = url ?? "",
            });

            return ToolResultHelper.Text(source);
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Generating the page object model failed: {ex.Message}");
        }
    }
}
