namespace Motus.Runner.Services.Timeline;

public sealed record NetworkCapture(string Url, string Method, int StatusCode, bool Failed);
