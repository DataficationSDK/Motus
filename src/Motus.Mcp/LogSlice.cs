namespace Motus.Mcp;

/// <summary>
/// One read of a bounded log: the entries from the cursor onwards, the cursor to read from next
/// time, and how many entries fell out of the log before this reader reached them.
/// </summary>
/// <remarks>
/// A log that empties itself when it is read cannot be read twice, which makes every reader the
/// only reader and turns a dropped reply into lost output. Reading by cursor instead means a
/// retry sees the same entries, two readers do not take them from each other, and an action can
/// say how much arrived while it ran by remembering the cursor it started at.
/// </remarks>
/// <typeparam name="TEntry">The type of entry the log holds.</typeparam>
/// <param name="Entries">The entries at or after the requested cursor, in arrival order.</param>
/// <param name="Next">The cursor to pass as <c>since</c> to read only what arrives after this read.</param>
/// <param name="Dropped">
/// How many entries between the requested cursor and the oldest entry still held were evicted to
/// keep the log bounded. Anything above zero means the reader missed something.
/// </param>
public sealed record LogSlice<TEntry>(IReadOnlyList<TEntry> Entries, long Next, int Dropped);
