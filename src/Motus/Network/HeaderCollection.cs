using System.Collections;
using Motus.Abstractions;

namespace Motus;

internal sealed class HeaderCollection : IHeaderCollection
{
    private readonly Dictionary<string, List<string>> _headers;

    internal HeaderCollection(Dictionary<string, string>? cdpHeaders)
    {
        _headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (cdpHeaders is not null)
        {
            foreach (var (key, value) in cdpHeaders)
                _headers[key] = [value];
        }
    }

    internal HeaderCollection(IEnumerable<FetchHeaderEntry>? entries)
    {
        _headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (entries is not null)
        {
            foreach (var entry in entries)
            {
                if (!_headers.TryGetValue(entry.Name, out var list))
                    _headers[entry.Name] = list = [];
                list.Add(entry.Value);
            }
        }
    }

    public string this[string name] =>
        _headers.TryGetValue(name, out var values) && values.Count > 0
            ? values[0]
            : throw new KeyNotFoundException($"Header '{name}' not found.");

    public IReadOnlyList<string> GetAll(string name) =>
        _headers.TryGetValue(name, out var values) ? values : [];

    public bool Contains(string name) => _headers.ContainsKey(name);

    public IEnumerator<KeyValuePair<string, IReadOnlyList<string>>> GetEnumerator() =>
        _headers
            .Select(kv => new KeyValuePair<string, IReadOnlyList<string>>(kv.Key, kv.Value))
            .GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal static FetchHeaderEntry[] ToFetchHeaders(IDictionary<string, string>? headers) =>
        headers?.Select(kv => new FetchHeaderEntry(kv.Key, kv.Value)).ToArray() ?? [];
}
