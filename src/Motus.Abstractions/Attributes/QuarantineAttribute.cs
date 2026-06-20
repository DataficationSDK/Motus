namespace Motus.Abstractions;

/// <summary>
/// Marks a test (or every test in a class) as quarantined. A quarantined test still
/// runs, but its failures do not fail the run: it is reported in a separate bucket and
/// excluded from the exit code. Use this to keep a known-flaky test running and visible
/// while it is being stabilized, instead of deleting or skipping it.
/// </summary>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Class,
    AllowMultiple = false,
    Inherited = true)]
public sealed class QuarantineAttribute : Attribute
{
    /// <summary>Optional reason describing why the test is quarantined.</summary>
    public string? Reason { get; set; }
}
