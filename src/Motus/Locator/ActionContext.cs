namespace Motus;

internal static class ActionContext
{
    internal static readonly AsyncLocal<string?> CurrentSelector = new();
}
