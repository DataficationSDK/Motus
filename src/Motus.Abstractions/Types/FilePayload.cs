namespace Motus.Abstractions;

/// <summary>
/// Represents a file to be uploaded via a file chooser.
/// </summary>
/// <param name="Name">The file name.</param>
/// <param name="MimeType">The MIME type of the file.</param>
/// <param name="Buffer">The file content as a byte array.</param>
public sealed record FilePayload(string Name, string MimeType, byte[] Buffer);
