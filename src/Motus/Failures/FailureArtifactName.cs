using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Motus;

/// <summary>
/// Names the files written when a test fails, so a trace or screenshot can be traced back to the
/// test that produced it.
/// </summary>
/// <remarks>
/// A test name can hold almost anything: data row arguments carry quotes, slashes, colons and angle
/// brackets, and generic types carry backticks. The file names built here keep a fixed set of
/// characters that every file system and CI artifact upload accepts, rather than the set the
/// current OS allows, so that a name written on Linux can still be unpacked on Windows.
/// </remarks>
internal static class FailureArtifactName
{
    /// <summary>
    /// The longest label kept whole, in UTF-8 bytes. A longer label is cut and given a hash, which
    /// keeps the whole file name well under the 255 byte limit most file systems set on one name.
    /// </summary>
    internal const int MaxLabelBytes = 100;

    // Where a long label is cut, leaving room for the separator and the hash after it.
    private const int TruncatedLabelBytes = 90;

    // How many numbered names are tried before giving up on a readable one.
    private const int MaxCollisionSuffix = 100;

    /// <summary>
    /// Reduces a test name to characters that are safe in a file name everywhere.
    /// </summary>
    /// <returns>The cleaned label, or null when nothing usable is left.</returns>
    internal static string? Sanitize(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        var builder = new StringBuilder(label.Length);
        foreach (var rune in label.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune) || rune.Value is '.' or '-' or '_')
            {
                // A run of underscores says nothing that one does not.
                if (rune.Value == '_' && builder.Length > 0 && builder[^1] == '_')
                    continue;

                builder.Append(rune.ToString());
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
        }

        // Windows will not create a name that ends in a dot, and a leading dot hides the file on
        // macOS and Linux.
        var cleaned = builder.ToString().Trim('_', '.', ' ');
        if (cleaned.Length == 0)
            return null;

        if (Encoding.UTF8.GetByteCount(cleaned) <= MaxLabelBytes)
            return cleaned;

        // Two long names can share their first ninety bytes, so the hash is what keeps them apart.
        // It is taken over the original name, which is the thing that actually differs.
        var cut = TruncateToBytes(cleaned, TruncatedLabelBytes).TrimEnd('_', '.');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(label));
        return $"{cut}~{Convert.ToHexString(hash, 0, 4).ToLowerInvariant()}";
    }

    /// <summary>
    /// Builds a file name of the form <c>{prefix}-{label}-{timestamp}.{extension}</c>.
    /// </summary>
    /// <param name="prefix">What the file is, such as <c>trace</c> or <c>failure</c>.</param>
    /// <param name="safeLabel">A label from <see cref="Sanitize"/>, or null for the unlabeled form.</param>
    /// <param name="extension">The extension, without its dot.</param>
    /// <param name="utcNow">The time to stamp the name with.</param>
    /// <remarks>
    /// Without a label the name is <c>{prefix}-{timestamp}.{extension}</c>, exactly as it was before
    /// labels existed, so anything that produces artifacts outside a test adapter is unaffected.
    /// </remarks>
    internal static string Build(string prefix, string? safeLabel, string extension, DateTime utcNow)
    {
        var stamp = utcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        return safeLabel is null
            ? $"{prefix}-{stamp}.{extension}"
            : $"{prefix}-{safeLabel}-{stamp}.{extension}";
    }

    /// <summary>
    /// Claims a file name in <paramref name="directory"/>, adding <c>-2</c>, <c>-3</c> and so on
    /// when it is taken, and returns the full path of the empty file it created.
    /// </summary>
    /// <remarks>
    /// The file is created rather than tested for, because the rows of one data driven test run in
    /// parallel, carry the same label, and can fail within the same millisecond. Two of them testing
    /// for a free name would both find it, and the second to write would overwrite the first.
    /// Whatever is written to the path afterwards replaces the empty file.
    /// </remarks>
    internal static string ReservePath(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (int attempt = 1; attempt <= MaxCollisionSuffix; attempt++)
        {
            var candidate = Path.Combine(directory, attempt == 1 ? fileName : $"{stem}-{attempt}{extension}");
            if (TryCreate(candidate))
                return Path.GetFullPath(candidate);
        }

        // A hundred failures at the same moment under one name is not a case worth a readable name,
        // only one that does not overwrite anything.
        var fallback = Path.Combine(directory, $"{stem}-{Guid.NewGuid():N}{extension}");
        using (new FileStream(fallback, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
        }

        return Path.GetFullPath(fallback);
    }

    private static bool TryCreate(string path)
    {
        try
        {
            using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
            }

            return true;
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
        }
    }

    private static string TruncateToBytes(string value, int maxBytes)
    {
        var builder = new StringBuilder();
        var bytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            bytes += rune.Utf8SequenceLength;
            if (bytes > maxBytes)
                break;

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }
}
