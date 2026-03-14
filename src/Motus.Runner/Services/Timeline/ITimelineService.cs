namespace Motus.Runner.Services.Timeline;

public interface ITimelineService
{
    IReadOnlyList<TimelineEntry> Entries { get; }
    int? SelectedIndex { get; }
    TimelineEntry? SelectedEntry { get; }
    string? CurrentTestName { get; set; }
    string? SelectedTestName { get; }
    event Action? TimelineChanged;
    event Action? TestSelected;
    void Clear();
    void AddEntry(TimelineEntry entry);
    void SelectEntry(int index);
    void ClearSelection();
    void SelectTest(string fullName);
}
