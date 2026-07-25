using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Motus.Testing.MSTest;

/// <summary>
/// A test class whose tests survive the browser going away, in place of <c>[TestClass]</c>.
/// </summary>
/// <remarks>
/// Every <c>[TestMethod]</c> in the class is run again if the browser it was driving disconnects,
/// which is what <see cref="MotusTestMethodAttribute"/> describes. Marking the class rather than
/// each method means a suite gets that for nothing, which is the point: the failure this guards
/// against belongs to the machine the tests ran on, not to any one test.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public class MotusTestClassAttribute : TestClassAttribute
{
    /// <summary>
    /// How many further attempts a lost browser is worth, for every test in the class. Zero runs
    /// each test once, whatever happens.
    /// </summary>
    public int Retries { get; set; } = 1;

    /// <inheritdoc />
    public override TestMethodAttribute? GetTestMethodAttribute(TestMethodAttribute? testMethodAttribute)
    {
        // A method that already asked for this keeps what it asked for, including its own count.
        if (testMethodAttribute is MotusTestMethodAttribute)
            return testMethodAttribute;

        return new MotusTestMethodAttribute(testMethodAttribute) { Retries = Retries };
    }
}
