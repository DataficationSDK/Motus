namespace Motus.Runner.Services.Timeline;

public interface ITimelineService
{
    IReadOnlyList<TimelineEntry> Entries { get; }
    int? SelectedIndex { get; }
    TimelineEntry? SelectedEntry { get; }
    event Action? TimelineChanged;
    void Clear();
    void AddEntry(TimelineEntry entry);
    void SelectEntry(int index);
    void ClearSelection();
}
