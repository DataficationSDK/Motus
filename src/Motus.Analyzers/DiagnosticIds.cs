namespace Motus.Analyzers;

internal static class DiagnosticIds
{
    public const string NonAwaitedCall = "MOT001";
    public const string HardcodedDelay = "MOT002";
    public const string FragileSelector = "MOT003";
    public const string MissingDisposal = "MOT004";
    public const string UnusedLocator = "MOT005";
    public const string DeprecatedSelector = "MOT006";
    public const string NavigationWait = "MOT007";
}
