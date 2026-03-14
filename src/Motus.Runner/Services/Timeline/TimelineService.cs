namespace Motus.Runner.Services.Timeline;

internal sealed class TimelineService : ITimelineService
{
    private readonly List<TimelineEntry> _entries = [];
    private int? _selectedIndex;

    public IReadOnlyList<TimelineEntry> Entries
    {
        get
        {
            lock (_entries)
                return _entries.ToList();
        }
    }

    public int? SelectedIndex
    {
        get
        {
            lock (_entries)
                return _selectedIndex;
        }
    }

    public TimelineEntry? SelectedEntry
    {
        get
        {
            lock (_entries)
                return _selectedIndex is { } idx && idx >= 0 && idx < _entries.Count
                    ? _entries[idx]
                    : null;
        }
    }

    public event Action? TimelineChanged;

    public void Clear()
    {
        lock (_entries)
        {
            _entries.Clear();
            _selectedIndex = null;
        }
        TimelineChanged?.Invoke();
    }

    public void AddEntry(TimelineEntry entry)
    {
        lock (_entries)
            _entries.Add(entry);
        TimelineChanged?.Invoke();
    }

    public void SelectEntry(int index)
    {
        lock (_entries)
        {
            if (index >= 0 && index < _entries.Count)
                _selectedIndex = index;
        }
        TimelineChanged?.Invoke();
    }

    public void ClearSelection()
    {
        lock (_entries)
            _selectedIndex = null;
        TimelineChanged?.Invoke();
    }
}
