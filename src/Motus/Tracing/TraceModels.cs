namespace Motus;

internal sealed record ScreenshotEntry(int Seq, byte[] JpegData);

internal sealed record ConsoleEntry(string Type, string Text, double Timestamp);
