using Motus.Codegen.Model;
using Motus.Codegen.Parser;

namespace Motus.Codegen.Tests;

[TestClass]
public class CdpSchemaParserTests
{
    private static string LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "minimal_protocol.json");
        return File.ReadAllText(path);
    }

    [TestMethod]
    public void Parse_ReturnsTwoDomains()
    {
        var domains = CdpSchemaParser.Parse(LoadFixture());
        Assert.AreEqual(2, domains.Length);
        Assert.AreEqual("TestDomain", domains[0].Name);
        Assert.AreEqual("CrossRef", domains[1].Name);
    }

    [TestMethod]
    public void Parse_TestDomain_HasCorrectTypeCounts()
    {
        var domain = CdpSchemaParser.Parse(LoadFixture())[0];
        Assert.AreEqual(5, domain.Types.Length);
        Assert.AreEqual(2, domain.Commands.Length);
        Assert.AreEqual(2, domain.Events.Length);
    }

    [TestMethod]
    public void Parse_StringAlias_IdentifiedCorrectly()
    {
        var domain = CdpSchemaParser.Parse(LoadFixture())[0];
        var requestId = domain.Types[0];
        Assert.AreEqual("RequestId", requestId.Id);
        Assert.AreEqual(CdpTypeKind.Alias, requestId.Kind);
        Assert.AreEqual("string", requestId.UnderlyingType);
    }

    [TestMethod]
    public void Parse_NumberAlias_IdentifiedCorrectly()
    {
        var domain = CdpSchemaParser.Parse(LoadFixture())[0];
        var timestamp = domain.Types[1];
        Assert.AreEqual("Timestamp", timestamp.Id);
        Assert.AreEqual(CdpTypeKind.Alias, timestamp.Kind);
        Assert.AreEqual("number", timestamp.UnderlyingType);
    }

    [TestMethod]
    public void Parse_StringEnum_HasValues()
    {
        var domain = CdpSchemaParser.Parse(LoadFixture())[0];
        var resourceType = domain.Types[2];
        Assert.AreEqual("ResourceType", resourceType.Id);
        Assert.AreEqual(CdpTypeKind.StringEnum, resourceType.Kind);
        Assert.AreEqual(4, resourceType.EnumValues.Length);
        Assert.AreEqual("document", resourceType.EnumValues[0]);
        Assert.AreEqual("script", resourceType.EnumValues[3]);
    }

    [TestMethod]
    public void Parse_ObjectType_HasProperties()
    {
        var domain = CdpSchemaParser.Parse(LoadFixture())[0];
        var frameInfo = domain.Types[3];
        Assert.AreEqual("FrameInfo", frameInfo.Id);
        Assert.AreEqual(CdpTypeKind.Object, frameInfo.Kind);
        Assert.AreEqual(4, frameInfo.Properties.Length);

        Assert.AreEqual("id", frameInfo.Properties[0].Name);
        Assert.IsFalse(frameInfo.Properties[0].Optional);

        Assert.AreEqual("parentId", frameInfo.Properties[1].Name);
        Assert.IsTrue(frameInfo.Properties[1].Optional);
    }

    [TestMethod]
    public void Parse_ArrayType_HasItemType()
    {
        var domain = CdpSchemaParser.Parse(LoadFixture())[0];
        var nodeList = domain.Types[4];
        Assert.AreEqual("NodeList", nodeList.Id);
        Assert.AreEqual(CdpTypeKind.ArrayType, nodeList.Kind);
        Assert.AreEqual("integer", nodeList.ArrayItemType);
    }

    [TestMethod]
    public void Parse_Command_HasParametersAndReturns()
    {
        var domain = CdpSchemaParser.Parse(LoadFixture())[0];
        var navigate = domain.Commands[0];
        Assert.AreEqual("navigate", navigate.Name);
        Assert.AreEqual(3, navigate.Parameters.Length);
        Assert.AreEqual(2, navigate.Returns.Length);

        // Check $ref parameter
        var transitionType = navigate.Parameters[2];
        Assert.AreEqual("ResourceType", transitionType.TypeRef);
        Assert.IsTrue(transitionType.Optional);
    }

    [TestMethod]
    public void Parse_EmptyCommand_HasNoParameters()
    {
        var domain = CdpSchemaParser.Parse(LoadFixture())[0];
        var disable = domain.Commands[1];
        Assert.AreEqual("disable", disable.Name);
        Assert.AreEqual(0, disable.Parameters.Length);
        Assert.AreEqual(0, disable.Returns.Length);
    }

    [TestMethod]
    public void Parse_Event_HasParameters()
    {
        var domain = CdpSchemaParser.Parse(LoadFixture())[0];
        var frameNavigated = domain.Events[0];
        Assert.AreEqual("frameNavigated", frameNavigated.Name);
        Assert.AreEqual(2, frameNavigated.Parameters.Length);
        Assert.AreEqual("FrameInfo", frameNavigated.Parameters[0].TypeRef);
    }

    [TestMethod]
    public void Parse_CrossDomainRef_HasCorrectRef()
    {
        var domains = CdpSchemaParser.Parse(LoadFixture());
        var crossRef = domains[1];
        var externalFrame = crossRef.Types[0];
        Assert.AreEqual("TestDomain.FrameInfo", externalFrame.Properties[0].TypeRef);
    }
}
