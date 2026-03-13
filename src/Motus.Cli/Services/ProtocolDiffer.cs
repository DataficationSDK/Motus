using System.Text.Json;

namespace Motus.Cli.Services;

public sealed record DomainDiff(
    string DomainName,
    IReadOnlyList<string> AddedCommands,
    IReadOnlyList<string> RemovedCommands,
    IReadOnlyList<string> AddedEvents,
    IReadOnlyList<string> RemovedEvents);

public sealed record ProtocolDiff(
    IReadOnlyList<string> AddedDomains,
    IReadOnlyList<string> RemovedDomains,
    IReadOnlyList<DomainDiff> ModifiedDomains)
{
    public bool HasChanges =>
        AddedDomains.Count > 0 || RemovedDomains.Count > 0 || ModifiedDomains.Count > 0;
}

public static class ProtocolDiffer
{
    public static ProtocolDiff Compare(string existingJson, string newJson)
    {
        using var existingDoc = JsonDocument.Parse(existingJson);
        using var newDoc = JsonDocument.Parse(newJson);

        var existingDomains = ExtractDomains(existingDoc);
        var newDomains = ExtractDomains(newDoc);

        var existingNames = existingDomains.Keys.ToHashSet();
        var newNames = newDomains.Keys.ToHashSet();

        var added = newNames.Except(existingNames).Order().ToList();
        var removed = existingNames.Except(newNames).Order().ToList();
        var common = existingNames.Intersect(newNames);

        var modified = new List<DomainDiff>();
        foreach (var domain in common.Order())
        {
            var oldInfo = existingDomains[domain];
            var newInfo = newDomains[domain];

            var addedCmds = newInfo.Commands.Except(oldInfo.Commands).Order().ToList();
            var removedCmds = oldInfo.Commands.Except(newInfo.Commands).Order().ToList();
            var addedEvts = newInfo.Events.Except(oldInfo.Events).Order().ToList();
            var removedEvts = oldInfo.Events.Except(newInfo.Events).Order().ToList();

            if (addedCmds.Count > 0 || removedCmds.Count > 0 || addedEvts.Count > 0 || removedEvts.Count > 0)
            {
                modified.Add(new DomainDiff(domain, addedCmds, removedCmds, addedEvts, removedEvts));
            }
        }

        return new ProtocolDiff(added, removed, modified);
    }

    private static Dictionary<string, DomainInfo> ExtractDomains(JsonDocument doc)
    {
        var result = new Dictionary<string, DomainInfo>();

        if (!doc.RootElement.TryGetProperty("domains", out var domains))
            return result;

        foreach (var domain in domains.EnumerateArray())
        {
            var name = domain.GetProperty("domain").GetString()!;
            var commands = new HashSet<string>();
            var events = new HashSet<string>();

            if (domain.TryGetProperty("commands", out var cmds))
            {
                foreach (var cmd in cmds.EnumerateArray())
                    commands.Add(cmd.GetProperty("name").GetString()!);
            }

            if (domain.TryGetProperty("events", out var evts))
            {
                foreach (var evt in evts.EnumerateArray())
                    events.Add(evt.GetProperty("name").GetString()!);
            }

            result[name] = new DomainInfo(commands, events);
        }

        return result;
    }

    private sealed record DomainInfo(HashSet<string> Commands, HashSet<string> Events);
}
