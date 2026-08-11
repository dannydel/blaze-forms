using BlazeForms.Definitions;
using BlazeForms.Internal;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers <see cref="FieldValidator"/> in isolation: required, format, and numeric-bound checks
/// against the real shipped English resx (PRD §6, §12), independent of any rendered component.
/// </summary>
public sealed class FieldValidatorTests
{
    private readonly FieldValidator _validator = new(RendererLocalization.Shared);

    [Fact]
    public void RequiredTextMessageIsRemedyWordedAndQuotesTheLabel()
    {
        var node = TestNodes.Create(NodeType.Text, label: "Date of birth", required: true);

        var message = _validator.Validate(node, value: null, isVisible: true);

        Assert.Equal("Enter a value for 'Date of birth'.", message);
    }

    [Fact]
    public void RequiredDateMessageMatchesThePrdExample()
    {
        var node = TestNodes.Create(NodeType.Date, label: "Date of birth", required: true);

        var message = _validator.Validate(node, value: null, isVisible: true);

        // The exact wording PRD §6 quotes as the acceptance example.
        Assert.Equal("Enter a date for 'Date of birth'.", message);
    }

    [Theory]
    [InlineData(NodeType.Select)]
    [InlineData(NodeType.Radio)]
    [InlineData(NodeType.YesNo)]
    public void RequiredChoiceMessageSaysSelectAnOption(NodeType nodeType)
    {
        var node = TestNodes.Create(nodeType, label: "Preferred contact method", required: true);

        var message = _validator.Validate(node, value: null, isVisible: true);

        Assert.Equal("Select an option for 'Preferred contact method'.", message);
    }

    [Fact]
    public void UnansweredNonRequiredFieldHasNoError()
    {
        var node = TestNodes.Create(NodeType.Text, label: "Middle name", required: false);

        var message = _validator.Validate(node, value: null, isVisible: true);

        Assert.Null(message);
    }

    [Fact]
    public void RequiredWhenVisibleBlocksOnlyWhileVisible()
    {
        var node = TestNodes.Create(NodeType.Text, label: "Detail") with { RequiredWhenVisible = true };

        Assert.Null(_validator.Validate(node, value: null, isVisible: false));
        Assert.Equal("Enter a value for 'Detail'.", _validator.Validate(node, value: null, isVisible: true));
    }

    [Fact]
    public void CalcNodeIsNeverValidatedEvenWhenMarkedRequired()
    {
        var node = TestNodes.Create(NodeType.Calc, label: "Estimate", required: true);

        Assert.Null(_validator.Validate(node, value: null, isVisible: true));
    }

    [Theory]
    [InlineData(NodeType.Heading)]
    [InlineData(NodeType.Paragraph)]
    [InlineData(NodeType.Callout)]
    [InlineData(NodeType.Divider)]
    public void StaticContentNodesAreNeverValidated(NodeType nodeType)
    {
        var node = TestNodes.Create(nodeType, required: true);

        Assert.Null(_validator.Validate(node, value: "anything", isVisible: true));
    }

    [Fact]
    public void EmptyEmailWhenRequiredSaysEnterAnEmailAddress()
    {
        var node = TestNodes.Create(NodeType.Email, label: "Email address", required: true);

        var message = _validator.Validate(node, value: null, isVisible: true);

        Assert.Equal("Enter an email address for 'Email address'.", message);
    }

    [Fact]
    public void ImplausibleEmailSaysEnterAValidEmailAddress()
    {
        var node = TestNodes.Create(NodeType.Email, label: "Email address");

        var message = _validator.Validate(node, value: "not-an-email", isVisible: true);

        Assert.Equal("Enter a valid email address for 'Email address'.", message);
    }

    [Fact]
    public void PlausibleEmailHasNoError()
    {
        var node = TestNodes.Create(NodeType.Email, label: "Email address");

        Assert.Null(_validator.Validate(node, value: "respondent@example.com", isVisible: true));
    }

    [Fact]
    public void ImplausiblePhoneSaysEnterAValidPhoneNumber()
    {
        var node = TestNodes.Create(NodeType.Phone, label: "Phone number");

        var message = _validator.Validate(node, value: "abc", isVisible: true);

        Assert.Equal("Enter a valid phone number for 'Phone number'.", message);
    }

    [Fact]
    public void PlausiblePhoneHasNoError()
    {
        var node = TestNodes.Create(NodeType.Phone, label: "Phone number");

        Assert.Null(_validator.Validate(node, value: "+1 (555) 123-4567", isVisible: true));
    }

    [Fact]
    public void BelowMinimumSaysEnterAValueOfTheMinimumOrMore()
    {
        var node = TestNodes.Create(NodeType.Number, label: "Age", min: 18);

        var message = _validator.Validate(node, value: 5m, isVisible: true);

        Assert.Equal("Enter a value of 18 or more for 'Age'.", message);
    }

    [Fact]
    public void AboveMaximumSaysEnterAValueOfTheMaximumOrLess()
    {
        var node = TestNodes.Create(NodeType.Currency, label: "Donation", max: 100);

        var message = _validator.Validate(node, value: 500m, isVisible: true);

        Assert.Equal("Enter a value of 100 or less for 'Donation'.", message);
    }

    [Fact]
    public void WithinBoundsHasNoError()
    {
        var node = TestNodes.Create(NodeType.Number, label: "Age", min: 18, max: 65);

        Assert.Null(_validator.Validate(node, value: 40m, isVisible: true));
    }

    [Fact]
    public void RequiredCheckboxGroupSaysSelectAtLeastOneOption()
    {
        var node = TestNodes.Create(NodeType.CheckboxGroup, label: "Interests", required: true);

        var message = _validator.Validate(node, value: new List<string>(), isVisible: true);

        Assert.Equal("Select at least one option for 'Interests'.", message);
    }

    [Fact]
    public void RequiredBooleanSaysSelectToContinue()
    {
        var node = TestNodes.Create(NodeType.Boolean, label: "I agree to the terms", required: true);

        var message = _validator.Validate(node, value: false, isVisible: true);

        Assert.Equal("Select 'I agree to the terms' to continue.", message);
    }

    [Fact]
    public void RequiredDateRangeSaysEnterAStartAndEndDate()
    {
        var node = TestNodes.Create(NodeType.DateRange, label: "Coverage period", required: true);
        string[] partiallyFilled = ["2026-01-01", ""];

        var message = _validator.Validate(node, partiallyFilled, isVisible: true);

        Assert.Equal("Enter a start and end date for 'Coverage period'.", message);
    }

    [Fact]
    public void ADateRangeWithBothSidesFilledHasNoError()
    {
        var node = TestNodes.Create(NodeType.DateRange, label: "Coverage period", required: true);
        string[] fullyFilled = ["2026-01-01", "2026-01-31"];

        Assert.Null(_validator.Validate(node, fullyFilled, isVisible: true));
    }
}
