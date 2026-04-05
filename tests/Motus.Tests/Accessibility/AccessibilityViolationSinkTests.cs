using Motus.Abstractions;

namespace Motus.Tests.Accessibility;

[TestClass]
public class AccessibilityViolationSinkTests
{
    private static AccessibilityViolation MakeViolation(string ruleId) =>
        new(ruleId, AccessibilityViolationSeverity.Error, $"Test {ruleId}", null, null, null, null);

    [TestMethod]
    public void Begin_Add_End_ReturnsCollectedViolations()
    {
        AccessibilityViolationSink.Begin();
        AccessibilityViolationSink.Add(MakeViolation("rule-1"));
        AccessibilityViolationSink.Add(MakeViolation("rule-2"));
        var result = AccessibilityViolationSink.End();

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("rule-1", result[0].RuleId);
        Assert.AreEqual("rule-2", result[1].RuleId);
    }

    [TestMethod]
    public void End_WithoutBegin_ReturnsEmpty()
    {
        var result = AccessibilityViolationSink.End();
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Add_WithoutBegin_DoesNotThrow()
    {
        AccessibilityViolationSink.Add(MakeViolation("rule-1"));
        var result = AccessibilityViolationSink.End();
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void End_ClearsState()
    {
        AccessibilityViolationSink.Begin();
        AccessibilityViolationSink.Add(MakeViolation("rule-1"));
        AccessibilityViolationSink.End();

        // Second End should return empty
        var result = AccessibilityViolationSink.End();
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task ParallelFlows_AreIsolated()
    {
        var violations1 = new List<string>();
        var violations2 = new List<string>();

        var task1 = Task.Run(() =>
        {
            AccessibilityViolationSink.Begin();
            AccessibilityViolationSink.Add(MakeViolation("flow-1-rule"));
            var result = AccessibilityViolationSink.End();
            lock (violations1)
                violations1.AddRange(result.Select(v => v.RuleId));
        });

        var task2 = Task.Run(() =>
        {
            AccessibilityViolationSink.Begin();
            AccessibilityViolationSink.Add(MakeViolation("flow-2-rule"));
            var result = AccessibilityViolationSink.End();
            lock (violations2)
                violations2.AddRange(result.Select(v => v.RuleId));
        });

        await Task.WhenAll(task1, task2);

        CollectionAssert.AreEqual(new[] { "flow-1-rule" }, violations1);
        CollectionAssert.AreEqual(new[] { "flow-2-rule" }, violations2);
    }
}
