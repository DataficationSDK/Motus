namespace Motus;

/// <summary>
/// Transport capability flags. Each flag indicates a protocol feature
/// that the active transport natively supports.
/// </summary>
[Flags]
internal enum MotusCapabilities
{
    None = 0,

    /// <summary>Supports CDP Target domain multiplexing.</summary>
    TargetMultiplexing = 1 << 0,

    /// <summary>Supports CDP Fetch domain for request interception.</summary>
    FetchInterception = 1 << 1,

    /// <summary>Supports CDP Emulation domain for device metrics.</summary>
    EmulationOverrides = 1 << 2,

    /// <summary>Supports CDP Tracing domain.</summary>
    Tracing = 1 << 3,

    /// <summary>Supports CDP Security domain (certificate error override).</summary>
    SecurityOverrides = 1 << 7,

    /// <summary>Supports CDP Accessibility domain (AX tree query).</summary>
    AccessibilityTree = 1 << 8,

    /// <summary>Supports WebDriver BiDi native network intercept.</summary>
    BiDiNetworkIntercept = 1 << 4,

    /// <summary>Supports WebDriver BiDi script evaluation.</summary>
    BiDiScriptEvaluation = 1 << 5,

    /// <summary>Supports WebDriver BiDi input actions.</summary>
    BiDiInputActions = 1 << 6,

    /// <summary>Full CDP feature set.</summary>
    AllCdp = TargetMultiplexing | FetchInterception | EmulationOverrides | Tracing
           | SecurityOverrides | AccessibilityTree,

    /// <summary>Full BiDi feature set.</summary>
    AllBiDi = BiDiNetworkIntercept | BiDiScriptEvaluation | BiDiInputActions
}

/// <summary>
/// Guards against unsupported transport capabilities with clear error messages.
/// </summary>
internal static class CapabilityGuard
{
    internal static void Require(
        MotusCapabilities has, MotusCapabilities required,
        string featureName, string? transportDescription = null)
    {
        if ((has & required) != required)
        {
            var message = transportDescription is not null
                ? $"'{featureName}' is not supported by the current browser transport ({transportDescription})."
                : $"The active transport does not support '{featureName}'.";
            throw new NotSupportedException(message);
        }
    }

    internal static string GetTransportDescription(IMotusTransport transport) => transport switch
    {
        CdpTransport => "Chrome/CDP",
        BiDiTransport => "Firefox/WebDriver BiDi",
        _ => transport.GetType().Name
    };

    internal static string GetTransportDescription(IMotusSession session) => session switch
    {
        CdpSession cdp => GetTransportDescription(cdp.Transport),
        BiDiSession bidi => GetTransportDescription(bidi.Transport),
        _ => session.GetType().Name
    };
}
