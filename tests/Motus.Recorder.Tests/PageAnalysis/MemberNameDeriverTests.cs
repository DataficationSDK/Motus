using Motus.Recorder.PageAnalysis;

namespace Motus.Recorder.Tests.PageAnalysis;

[TestClass]
public class MemberNameDeriverTests
{
    private static PageElementInfo MakeElement(
        string tag = "input",
        string? type = "text",
        string? id = null,
        string? name = null,
        string? ariaLabel = null,
        string? placeholder = null,
        string? text = null,
        string? role = null,
        int elementIndex = 0)
        => new(tag, type, id, name, ariaLabel, placeholder, text, null, role, null, null, elementIndex);

    [TestMethod]
    public void DeriveBaseName_PrefersId()
    {
        var el = MakeElement(id: "email-field", name: "email");
        Assert.AreEqual("EmailField", MemberNameDeriver.DeriveBaseName(el));
    }

    [TestMethod]
    public void DeriveBaseName_FallsToName_WhenNoId()
    {
        var el = MakeElement(name: "user_name");
        Assert.AreEqual("UserName", MemberNameDeriver.DeriveBaseName(el));
    }

    [TestMethod]
    public void DeriveBaseName_FallsToAriaLabel()
    {
        var el = MakeElement(ariaLabel: "Search input");
        Assert.AreEqual("SearchInput", MemberNameDeriver.DeriveBaseName(el));
    }

    [TestMethod]
    public void DeriveBaseName_FallsToPlaceholder()
    {
        var el = MakeElement(placeholder: "Enter your email");
        Assert.AreEqual("EnterYourEmail", MemberNameDeriver.DeriveBaseName(el));
    }

    [TestMethod]
    public void DeriveBaseName_FallsToText()
    {
        var el = MakeElement(tag: "button", type: null, text: "Sign In");
        Assert.AreEqual("SignIn", MemberNameDeriver.DeriveBaseName(el));
    }

    [TestMethod]
    public void DeriveBaseName_PositionalFallback()
    {
        var el = MakeElement(elementIndex: 7);
        Assert.AreEqual("Element7", MemberNameDeriver.DeriveBaseName(el));
    }

    [TestMethod]
    public void GetTypeSuffix_InputText_ReturnsInput()
    {
        var el = MakeElement(tag: "input", type: "text");
        Assert.AreEqual("Input", MemberNameDeriver.GetTypeSuffix(el));
    }

    [TestMethod]
    public void GetTypeSuffix_InputCheckbox_ReturnsCheckbox()
    {
        var el = MakeElement(tag: "input", type: "checkbox");
        Assert.AreEqual("Checkbox", MemberNameDeriver.GetTypeSuffix(el));
    }

    [TestMethod]
    public void GetTypeSuffix_InputRadio_ReturnsRadio()
    {
        var el = MakeElement(tag: "input", type: "radio");
        Assert.AreEqual("Radio", MemberNameDeriver.GetTypeSuffix(el));
    }

    [TestMethod]
    public void GetTypeSuffix_InputSubmit_ReturnsButton()
    {
        var el = MakeElement(tag: "input", type: "submit");
        Assert.AreEqual("Button", MemberNameDeriver.GetTypeSuffix(el));
    }

    [TestMethod]
    public void GetTypeSuffix_Button_ReturnsButton()
    {
        var el = MakeElement(tag: "button", type: null);
        Assert.AreEqual("Button", MemberNameDeriver.GetTypeSuffix(el));
    }

    [TestMethod]
    public void GetTypeSuffix_Select_ReturnsDropdown()
    {
        var el = MakeElement(tag: "select", type: null);
        Assert.AreEqual("Dropdown", MemberNameDeriver.GetTypeSuffix(el));
    }

    [TestMethod]
    public void GetTypeSuffix_Anchor_ReturnsLink()
    {
        var el = MakeElement(tag: "a", type: null);
        Assert.AreEqual("Link", MemberNameDeriver.GetTypeSuffix(el));
    }

    [TestMethod]
    public void GetTypeSuffix_RoleButton_ReturnsButton()
    {
        var el = MakeElement(tag: "div", type: null, role: "button");
        Assert.AreEqual("Button", MemberNameDeriver.GetTypeSuffix(el));
    }

    [TestMethod]
    public void DeriveNames_DeduplicatesWithNumericSuffix()
    {
        var elements = new[]
        {
            MakeElement(id: "email", elementIndex: 0),
            MakeElement(id: "email", elementIndex: 1),
            MakeElement(id: "email", elementIndex: 2),
        };

        var names = MemberNameDeriver.DeriveNames(elements);
        Assert.AreEqual("EmailInput", names[0]);
        Assert.AreEqual("EmailInput2", names[1]);
        Assert.AreEqual("EmailInput3", names[2]);
    }

    [TestMethod]
    public void ToPascalCase_HandlesHyphensAndUnderscores()
    {
        Assert.AreEqual("MyFieldName", MemberNameDeriver.ToPascalCase("my-field_name"));
    }

    [TestMethod]
    public void ToPascalCase_HandlesLeadingDigit()
    {
        Assert.AreEqual("_123Field", MemberNameDeriver.ToPascalCase("123-field"));
    }

    [TestMethod]
    public void ToPascalCase_EmptyString_ReturnsElement()
    {
        Assert.AreEqual("Element", MemberNameDeriver.ToPascalCase(""));
    }
}
