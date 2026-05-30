using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// Element-level interactions beyond click and type: selecting options, hovering,
/// pressing keys, toggling checkboxes, clearing and focusing fields, scrolling an
/// element into view, uploading files, and waiting for a condition. Element tools
/// address their target by a ref from the latest <c>snapshot</c>, exactly as the
/// core tools do.
/// </summary>
/// <remarks>
/// Like the core tools, every tool reports failure as a result with
/// <see cref="CallToolResult.IsError"/> set and a message the model can act on,
/// rather than by throwing. Using a ref before any snapshot, or one from a stale
/// snapshot, returns guidance to snapshot again.
/// </remarks>
[McpServerToolType]
public sealed class InteractionTools
{
    [McpServerTool(Name = "select_option", Title = "Select dropdown options", Destructive = true)]
    [Description("Selects one or more options in a <select> element addressed by a ref, by their values.")]
    public static Task<CallToolResult> SelectOptionAsync(
        [Description("The element ref from the latest snapshot, e.g. e7.")] string @ref,
        [Description("The option values to select.")] string[] values,
        ActivePageService pageService,
        CancellationToken cancellationToken)
        => WithRefAsync(pageService, @ref, $"Selected {values.Length} option(s) in {@ref}",
            locator => locator.SelectOptionAsync(values), cancellationToken);

    [McpServerTool(Name = "hover", Title = "Hover over element", Destructive = true)]
    [Description("Moves the pointer over the element addressed by a ref from the latest snapshot.")]
    public static Task<CallToolResult> HoverAsync(
        [Description("The element ref from the latest snapshot, e.g. e7.")] string @ref,
        ActivePageService pageService,
        CancellationToken cancellationToken)
        => WithRefAsync(pageService, @ref, $"Hovered {@ref}",
            locator => locator.HoverAsync(), cancellationToken);

    [McpServerTool(Name = "press", Title = "Press a key on element", Destructive = true)]
    [Description("Presses a key while the element addressed by a ref is focused, e.g. Enter, Tab, ArrowDown.")]
    public static Task<CallToolResult> PressAsync(
        [Description("The element ref from the latest snapshot, e.g. e7.")] string @ref,
        [Description("The key to press, e.g. Enter, Tab, Escape, ArrowDown, or a single character.")] string key,
        ActivePageService pageService,
        CancellationToken cancellationToken)
        => WithRefAsync(pageService, @ref, $"Pressed {key} on {@ref}",
            locator => locator.PressAsync(key), cancellationToken);

    [McpServerTool(Name = "set_checked", Title = "Set checkbox state", Destructive = true)]
    [Description("Sets the checked state of a checkbox or radio button addressed by a ref.")]
    public static Task<CallToolResult> SetCheckedAsync(
        [Description("The element ref from the latest snapshot, e.g. e7.")] string @ref,
        [Description("The desired state: true to check, false to uncheck.")] bool @checked,
        ActivePageService pageService,
        CancellationToken cancellationToken)
        => WithRefAsync(pageService, @ref, $"Set {@ref} checked={@checked}",
            locator => locator.SetCheckedAsync(@checked), cancellationToken);

    [McpServerTool(Name = "clear", Title = "Clear input", Destructive = true)]
    [Description("Clears the value of the input or textarea addressed by a ref from the latest snapshot.")]
    public static Task<CallToolResult> ClearAsync(
        [Description("The element ref from the latest snapshot, e.g. e7.")] string @ref,
        ActivePageService pageService,
        CancellationToken cancellationToken)
        => WithRefAsync(pageService, @ref, $"Cleared {@ref}",
            locator => locator.ClearAsync(), cancellationToken);

    [McpServerTool(Name = "focus", Title = "Focus element", Destructive = true)]
    [Description("Focuses the element addressed by a ref from the latest snapshot.")]
    public static Task<CallToolResult> FocusAsync(
        [Description("The element ref from the latest snapshot, e.g. e7.")] string @ref,
        ActivePageService pageService,
        CancellationToken cancellationToken)
        => WithRefAsync(pageService, @ref, $"Focused {@ref}",
            locator => locator.FocusAsync(), cancellationToken);

    [McpServerTool(Name = "scroll_into_view", Title = "Scroll element into view", Destructive = true)]
    [Description("Scrolls the element addressed by a ref into the viewport if it is not already visible.")]
    public static Task<CallToolResult> ScrollIntoViewAsync(
        [Description("The element ref from the latest snapshot, e.g. e7.")] string @ref,
        ActivePageService pageService,
        CancellationToken cancellationToken)
        => WithRefAsync(pageService, @ref, $"Scrolled {@ref} into view",
            locator => locator.ScrollIntoViewIfNeededAsync(), cancellationToken);

    [McpServerTool(Name = "upload_files", Title = "Upload files", Destructive = true)]
    [Description("Sets the files of a file input addressed by a ref, reading each from a local file path.")]
    public static async Task<CallToolResult> UploadFilesAsync(
        [Description("The element ref from the latest snapshot, e.g. e7.")] string @ref,
        [Description("Local file paths to upload. Each is read from disk on the machine running the server.")] string[] paths,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        var payloads = new List<FilePayload>(paths.Length);
        foreach (var path in paths)
        {
            byte[] bytes;
            try
            {
                bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return ToolResultHelper.Error($"Could not read file '{path}': {ex.Message}");
            }

            payloads.Add(new FilePayload(Path.GetFileName(path), MimeTypeForExtension(path), bytes));
        }

        return await WithRefAsync(pageService, @ref, $"Uploaded {payloads.Count} file(s) to {@ref}",
            locator => locator.SetInputFilesAsync(payloads), cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "press_key", Title = "Press a key", Destructive = true)]
    [Description("Presses a key on the active page without targeting an element, e.g. Escape, Tab, Enter. "
        + "No snapshot is required.")]
    public static async Task<CallToolResult> PressKeyAsync(
        [Description("The key to press, e.g. Escape, Tab, Enter, ArrowDown, or a single character.")] string key,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            await page.Keyboard.PressAsync(key).ConfigureAwait(false);
            return ToolResultHelper.Text($"Pressed {key}");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Press key failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "wait_for_element", Title = "Wait for element state", Destructive = false, ReadOnly = true)]
    [Description("Waits until the element addressed by a ref reaches a state: visible, hidden, attached, or detached.")]
    public static async Task<CallToolResult> WaitForElementAsync(
        [Description("The element ref from the latest snapshot, e.g. e7.")] string @ref,
        [Description("The state to wait for: visible, hidden, attached, or detached.")] string state,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ElementState>(state, ignoreCase: true, out var parsed))
            return ToolResultHelper.Error(
                $"Unknown state '{state}'. Use one of: visible, hidden, attached, detached.");

        return await WithRefAsync(pageService, @ref, $"{@ref} reached state {parsed}",
            locator => locator.WaitForAsync(parsed), cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "wait_for", Title = "Wait for a page condition", Destructive = false, ReadOnly = true)]
    [Description("Waits for a page condition: a fixed time, text to appear, or text to disappear. "
        + "Provide exactly one of time, text, or text_gone.")]
    public static async Task<CallToolResult> WaitForAsync(
        [Description("Time to wait in milliseconds.")] int? time,
        [Description("Wait until this text appears anywhere on the page.")] string? text,
        [Description("Wait until this text is gone from the page.")] string? text_gone,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);

            if (time is { } ms)
            {
                await page.WaitForTimeoutAsync(ms).ConfigureAwait(false);
                return ToolResultHelper.Text($"Waited {ms} ms");
            }

            if (text is not null)
            {
                await page.WaitForFunctionAsync<bool>(
                    "(t) => !!document.body && document.body.innerText.includes(t)", text).ConfigureAwait(false);
                return ToolResultHelper.Text($"Text appeared: {text}");
            }

            if (text_gone is not null)
            {
                await page.WaitForFunctionAsync<bool>(
                    "(t) => !document.body || !document.body.innerText.includes(t)", text_gone).ConfigureAwait(false);
                return ToolResultHelper.Text($"Text gone: {text_gone}");
            }

            return ToolResultHelper.Error("Provide one of time, text, or text_gone.");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Wait failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves a ref to a locator, runs an action against it, and maps the snapshot
    /// error contract to re-snapshot guidance. Shared by every ref-addressed tool.
    /// </summary>
    private static async Task<CallToolResult> WithRefAsync(
        ActivePageService pageService,
        string @ref,
        string okText,
        Func<ILocator, Task> action,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            var locator = pageService.GetSnapshotService(page).ResolveRef(@ref);
            await action(locator).ConfigureAwait(false);
            return ToolResultHelper.Text(okText);
        }
        catch (SnapshotNotTakenException)
        {
            return ToolResultHelper.NoSnapshot();
        }
        catch (StaleRefException ex)
        {
            return ToolResultHelper.Stale(ex);
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"{@ref}: {ex.Message}");
        }
    }

    /// <summary>
    /// Maps a file extension to a MIME type for upload. A small built-in table covers
    /// the common cases; anything else falls back to a generic binary type. Kept
    /// dependency-free so no package or Node runtime is pulled into the server path.
    /// </summary>
    private static string MimeTypeForExtension(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".html" or ".htm" => "text/html",
            ".xml" => "application/xml",
            ".zip" => "application/zip",
            _ => "application/octet-stream",
        };
}
