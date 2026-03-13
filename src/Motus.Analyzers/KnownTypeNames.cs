namespace Motus.Analyzers;

internal static class KnownTypeNames
{
    public const string IPage = "Motus.Abstractions.IPage";
    public const string ILocator = "Motus.Abstractions.ILocator";
    public const string IBrowser = "Motus.Abstractions.IBrowser";
    public const string IBrowserContext = "Motus.Abstractions.IBrowserContext";
    public const string IFrame = "Motus.Abstractions.IFrame";

    public const string Task = "System.Threading.Tasks.Task";
    public const string TaskOfT = "System.Threading.Tasks.Task`1";

    public const string ThreadType = "System.Threading.Thread";
    public const string TaskType = "System.Threading.Tasks.Task";
}
