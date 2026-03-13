using Motus.Cli.Services;

namespace Motus.Cli.Tests.Services;

[TestClass]
public class ProtocolDifferTests
{
    [TestMethod]
    public void Compare_IdenticalProtocols_NoDiff()
    {
        var json = """
        {
            "domains": [
                {
                    "domain": "Page",
                    "commands": [{ "name": "navigate" }, { "name": "reload" }],
                    "events": [{ "name": "loadEventFired" }]
                }
            ]
        }
        """;

        var diff = ProtocolDiffer.Compare(json, json);

        Assert.IsFalse(diff.HasChanges);
        Assert.AreEqual(0, diff.AddedDomains.Count);
        Assert.AreEqual(0, diff.RemovedDomains.Count);
        Assert.AreEqual(0, diff.ModifiedDomains.Count);
    }

    [TestMethod]
    public void Compare_AddedDomain_Detected()
    {
        var existing = """{ "domains": [{ "domain": "Page", "commands": [], "events": [] }] }""";
        var updated = """
        {
            "domains": [
                { "domain": "Page", "commands": [], "events": [] },
                { "domain": "Network", "commands": [], "events": [] }
            ]
        }
        """;

        var diff = ProtocolDiffer.Compare(existing, updated);

        Assert.IsTrue(diff.HasChanges);
        Assert.AreEqual(1, diff.AddedDomains.Count);
        Assert.AreEqual("Network", diff.AddedDomains[0]);
    }

    [TestMethod]
    public void Compare_RemovedDomain_Detected()
    {
        var existing = """
        {
            "domains": [
                { "domain": "Page", "commands": [], "events": [] },
                { "domain": "Network", "commands": [], "events": [] }
            ]
        }
        """;
        var updated = """{ "domains": [{ "domain": "Page", "commands": [], "events": [] }] }""";

        var diff = ProtocolDiffer.Compare(existing, updated);

        Assert.IsTrue(diff.HasChanges);
        Assert.AreEqual(1, diff.RemovedDomains.Count);
        Assert.AreEqual("Network", diff.RemovedDomains[0]);
    }

    [TestMethod]
    public void Compare_AddedCommand_Detected()
    {
        var existing = """
        {
            "domains": [{
                "domain": "Page",
                "commands": [{ "name": "navigate" }],
                "events": []
            }]
        }
        """;
        var updated = """
        {
            "domains": [{
                "domain": "Page",
                "commands": [{ "name": "navigate" }, { "name": "reload" }],
                "events": []
            }]
        }
        """;

        var diff = ProtocolDiffer.Compare(existing, updated);

        Assert.IsTrue(diff.HasChanges);
        Assert.AreEqual(1, diff.ModifiedDomains.Count);
        Assert.AreEqual("Page", diff.ModifiedDomains[0].DomainName);
        Assert.AreEqual(1, diff.ModifiedDomains[0].AddedCommands.Count);
        Assert.AreEqual("reload", diff.ModifiedDomains[0].AddedCommands[0]);
    }

    [TestMethod]
    public void Compare_RemovedEvent_Detected()
    {
        var existing = """
        {
            "domains": [{
                "domain": "Page",
                "commands": [],
                "events": [{ "name": "loadEventFired" }, { "name": "frameNavigated" }]
            }]
        }
        """;
        var updated = """
        {
            "domains": [{
                "domain": "Page",
                "commands": [],
                "events": [{ "name": "loadEventFired" }]
            }]
        }
        """;

        var diff = ProtocolDiffer.Compare(existing, updated);

        Assert.IsTrue(diff.HasChanges);
        Assert.AreEqual(1, diff.ModifiedDomains[0].RemovedEvents.Count);
        Assert.AreEqual("frameNavigated", diff.ModifiedDomains[0].RemovedEvents[0]);
    }

    [TestMethod]
    public void Compare_EmptyDomains_NoDiff()
    {
        var json = """{ "domains": [] }""";
        var diff = ProtocolDiffer.Compare(json, json);
        Assert.IsFalse(diff.HasChanges);
    }

    [TestMethod]
    public void Compare_MixedChanges_AllDetected()
    {
        var existing = """
        {
            "domains": [
                { "domain": "Page", "commands": [{ "name": "navigate" }], "events": [{ "name": "load" }] },
                { "domain": "DOM", "commands": [], "events": [] }
            ]
        }
        """;
        var updated = """
        {
            "domains": [
                { "domain": "Page", "commands": [{ "name": "navigate" }, { "name": "close" }], "events": [] },
                { "domain": "Network", "commands": [], "events": [] }
            ]
        }
        """;

        var diff = ProtocolDiffer.Compare(existing, updated);

        Assert.IsTrue(diff.HasChanges);
        Assert.AreEqual(1, diff.AddedDomains.Count);
        Assert.AreEqual("Network", diff.AddedDomains[0]);
        Assert.AreEqual(1, diff.RemovedDomains.Count);
        Assert.AreEqual("DOM", diff.RemovedDomains[0]);
        Assert.AreEqual(1, diff.ModifiedDomains.Count);
        Assert.AreEqual("close", diff.ModifiedDomains[0].AddedCommands[0]);
        Assert.AreEqual("load", diff.ModifiedDomains[0].RemovedEvents[0]);
    }
}
