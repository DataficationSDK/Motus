using Motus.Recorder.PageAnalysis;

namespace Motus.Recorder.PomEmit;

/// <summary>
/// Groups discovered elements by form index and identifies form action candidates.
/// </summary>
internal static class FormGrouper
{
    internal sealed record FormGroup(
        int FormIndex,
        IReadOnlyList<DiscoveredElement> FillableInputs,
        DiscoveredElement? SubmitButton);

    /// <summary>
    /// Clusters elements by their form index and returns groups that contain
    /// at least one fillable input and a submit button.
    /// </summary>
    internal static IReadOnlyList<FormGroup> GroupByForm(IReadOnlyList<DiscoveredElement> elements)
    {
        var groups = new Dictionary<int, (List<DiscoveredElement> Inputs, DiscoveredElement? Submit)>();

        foreach (var el in elements)
        {
            if (el.Info.FormIndex is not { } formIndex)
                continue;
            if (el.Selector is null)
                continue;

            if (!groups.TryGetValue(formIndex, out var group))
            {
                group = (new List<DiscoveredElement>(), null);
                groups[formIndex] = group;
            }

            if (IsFillableInput(el.Info))
            {
                group.Inputs.Add(el);
            }
            else if (IsSubmitButton(el.Info) && group.Submit is null)
            {
                groups[formIndex] = (group.Inputs, el);
            }
        }

        var result = new List<FormGroup>();
        foreach (var (formIndex, (inputs, submit)) in groups)
        {
            if (inputs.Count > 0 && submit is not null)
                result.Add(new FormGroup(formIndex, inputs, submit));
        }

        return result;
    }

    private static bool IsFillableInput(PageElementInfo info)
    {
        var tag = info.Tag.ToLowerInvariant();
        if (tag == "select") return false;

        if (tag != "input" && tag != "textarea") return false;

        var type = info.Type?.ToLowerInvariant();
        return type is null or "text" or "email" or "password" or "search" or "tel" or "url" or "number";
    }

    private static bool IsSubmitButton(PageElementInfo info)
    {
        var tag = info.Tag.ToLowerInvariant();
        var type = info.Type?.ToLowerInvariant();

        if (tag == "button") return true;
        if (tag == "input" && type is "submit" or "button" or "image") return true;
        if (info.Role?.Equals("button", StringComparison.OrdinalIgnoreCase) == true) return true;

        return false;
    }
}
