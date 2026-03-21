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

    /// <summary>Supports WebDriver BiDi native network intercept.</summary>
    BiDiNetworkIntercept = 1 << 4,

    /// <summary>Full CDP feature set.</summary>
    AllCdp = TargetMultiplexing | FetchInterception | EmulationOverrides | Tracing
}

/// <summary>
/// Guards against unsupported transport capabilities with clear error messages.
/// </summary>
internal static class CapabilityGuard
{
    internal static void Require(MotusCapabilities has, MotusCapabilities required, string featureName)
    {
        if ((has & required) != required)
            throw new NotSupportedException(
                $"The active transport does not support '{featureName}'.");
    }
}
