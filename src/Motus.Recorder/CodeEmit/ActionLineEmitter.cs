using System.Text;
using Motus.Recorder.Records;

namespace Motus.Recorder.CodeEmit;

/// <summary>
/// Emits a single line of C# code for a <see cref="ResolvedAction"/>.
/// </summary>
internal static class ActionLineEmitter
{
    internal static string Emit(ResolvedAction resolved, string indent)
    {
        var action = resolved.Source;
        var selector = resolved.Selector;

        return action switch
        {
            ClickAction click => EmitClick(click, selector, indent),
            FillAction fill => EmitFill(fill, selector, indent),
            KeyPressAction key => $"{indent}await page.Keyboard.PressAsync({Escape(key.Key)});",
            NavigationAction nav => $"{indent}await page.GotoAsync({Escape(nav.Url)});",
            SelectAction sel => EmitSelect(sel, selector, indent),
            CheckAction check => EmitCheck(check, selector, indent),
            FileUploadAction upload => EmitFileUpload(upload, selector, indent),
            DialogAction dialog => EmitDialog(dialog, indent),
            ScrollAction scroll => EmitScroll(scroll, indent),
            _ => $"{indent}// Unknown action type: {action.GetType().Name}"
        };
    }

    private static string EmitClick(ClickAction click, string? selector, string indent)
    {
        if (selector is null)
            return $"{indent}// TODO: Click at ({click.X}, {click.Y}) - selector inference failed";

        return $"{indent}await page.Locator({Escape(selector)}).ClickAsync();";
    }

    private static string EmitFill(FillAction fill, string? selector, string indent)
    {
        if (selector is null)
            return $"{indent}// TODO: Fill at ({fill.X}, {fill.Y}) with {Escape(fill.Value)} - selector inference failed";

        return $"{indent}await page.Locator({Escape(selector)}).FillAsync({Escape(fill.Value)});";
    }

    private static string EmitSelect(SelectAction sel, string? selector, string indent)
    {
        if (selector is null)
            return $"{indent}// TODO: Select at ({sel.X}, {sel.Y}) - selector inference failed";

        if (sel.SelectedValues.Length == 1)
            return $"{indent}await page.Locator({Escape(selector)}).SelectOptionAsync({Escape(sel.SelectedValues[0])});";

        var values = string.Join(", ", sel.SelectedValues.Select(Escape));
        return $"{indent}await page.Locator({Escape(selector)}).SelectOptionAsync(new[] {{ {values} }});";
    }

    private static string EmitCheck(CheckAction check, string? selector, string indent)
    {
        if (selector is null)
            return $"{indent}// TODO: {(check.Checked ? "Check" : "Uncheck")} at ({check.X}, {check.Y}) - selector inference failed";

        var method = check.Checked ? "CheckAsync" : "UncheckAsync";
        return $"{indent}await page.Locator({Escape(selector)}).{method}();";
    }

    private static string EmitFileUpload(FileUploadAction upload, string? selector, string indent)
    {
        if (selector is null)
            return $"{indent}// TODO: File upload at ({upload.X}, {upload.Y}) - selector inference failed";

        if (upload.FileNames.Length == 0)
            return $"{indent}await page.Locator({Escape(selector)}).SetInputFilesAsync(Array.Empty<string>());";

        if (upload.FileNames.Length == 1)
            return $"{indent}await page.Locator({Escape(selector)}).SetInputFilesAsync({Escape(upload.FileNames[0])});";

        var files = string.Join(", ", upload.FileNames.Select(Escape));
        return $"{indent}await page.Locator({Escape(selector)}).SetInputFilesAsync(new[] {{ {files} }});";
    }

    private static string EmitScroll(ScrollAction scroll, string indent)
    {
        if (scroll.X is not null && scroll.Y is not null)
            return $"{indent}await page.Mouse.MoveAsync({scroll.X.Value}, {scroll.Y.Value});\n" +
                   $"{indent}await page.Mouse.WheelAsync({scroll.ScrollX}, {scroll.ScrollY});";

        return $"{indent}await page.Mouse.WheelAsync({scroll.ScrollX}, {scroll.ScrollY});";
    }

    private static string EmitDialog(DialogAction dialog, string indent)
    {
        if (dialog.Accepted)
        {
            if (dialog.PromptText is not null)
                return $"{indent}page.Dialog += (_, d) => d.AcceptAsync({Escape(dialog.PromptText)});";

            return $"{indent}page.Dialog += (_, d) => d.AcceptAsync();";
        }

        return $"{indent}page.Dialog += (_, d) => d.DismissAsync();";
    }

    internal static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
