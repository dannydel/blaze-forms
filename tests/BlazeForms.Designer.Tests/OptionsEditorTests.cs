using AngleSharp.Dom;
using BlazeForms.Definitions;
using BlazeForms.Properties;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="OptionsEditor"/>: a keyed row list where each option's stable
/// <see cref="FormOption.Value"/> follows its row through label edits, reorders, and the removal
/// of other rows (AGENTS.md invariant #5) — the regression this replaces a position-keyed
/// textarea's silent value reassignment on remove-first/middle and reorder — plus a single commit
/// per edit (never per keystroke) and the a11y contract on the row controls.
/// </summary>
public sealed class OptionsEditorTests : DesignerTestContext
{
    private static readonly IReadOnlyList<FormOption> TwoOptions =
    [
        new FormOption { Value = "opt-1", Label = "Option one" },
        new FormOption { Value = "opt-2", Label = "Option two" },
    ];

    private static readonly IReadOnlyList<FormOption> ThreeOptions =
    [
        new FormOption { Value = "opt-a", Label = "Active" },
        new FormOption { Value = "opt-b", Label = "Pending" },
        new FormOption { Value = "opt-c", Label = "Closed" },
    ];

    private static IReadOnlyList<IElement> RowInputs(IRenderedComponent<OptionsEditor> cut) =>
        cut.FindAll("input.bf-options-editor__row-input");

    private static IElement RemoveButtonFor(IRenderedComponent<OptionsEditor> cut, int rowNumber) =>
        cut.Find($"button[aria-label='Remove option {rowNumber}']");

    private static IElement MoveUpButtonFor(IRenderedComponent<OptionsEditor> cut, int rowNumber) =>
        cut.Find($"button[aria-label='Move option {rowNumber} up']");

    private static IElement MoveDownButtonFor(IRenderedComponent<OptionsEditor> cut, int rowNumber) =>
        cut.Find($"button[aria-label='Move option {rowNumber} down']");

    [Fact]
    public void RendersOneRowPerOptionWithItsOwnLabel()
    {
        var cut = Render<OptionsEditor>(p => p.Add(f => f.Options, TwoOptions));

        var inputs = RowInputs(cut);
        Assert.Equal(["Option one", "Option two"], inputs.Select(i => i.GetAttribute("value")));
    }

    [Fact]
    public async Task EditingAnOptionsLabelPreservesItsExistingValue()
    {
        IReadOnlyList<FormOption>? captured = null;
        var cut = Render<OptionsEditor>(p => p
            .Add(f => f.Options, TwoOptions)
            .Add(f => f.OptionsChanged, EventCallback.Factory.Create<IReadOnlyList<FormOption>>(this, list => captured = list)));

        await RowInputs(cut)[0].ChangeAsync(new ChangeEventArgs { Value = "Option ONE" });

        Assert.NotNull(captured);
        Assert.Equal("opt-1", captured![0].Value);
        Assert.Equal("Option ONE", captured[0].Label);
        Assert.Equal("opt-2", captured[1].Value);
        Assert.Equal("Option two", captured[1].Label);
    }

    [Fact]
    public async Task RemovingTheFirstRowKeepsEverySurvivorsOwnValue()
    {
        // The regression this editor fixes: [Active=opt-a, Pending=opt-b, Closed=opt-c], remove
        // the FIRST row. A position-keyed textarea would silently reassign opt-a to "Pending" and
        // opt-b to "Closed"; this editor must not.
        IReadOnlyList<FormOption>? captured = null;
        var cut = Render<OptionsEditor>(p => p
            .Add(f => f.Options, ThreeOptions)
            .Add(f => f.OptionsChanged, EventCallback.Factory.Create<IReadOnlyList<FormOption>>(this, list => captured = list)));

        await RemoveButtonFor(cut, 1).ClickAsync(new MouseEventArgs());

        Assert.NotNull(captured);
        Assert.Equal(2, captured!.Count);
        Assert.Equal(["opt-b", "opt-c"], captured.Select(o => o.Value));
        Assert.Equal(["Pending", "Closed"], captured.Select(o => o.Label));
    }

    [Fact]
    public async Task RemovingTheMiddleRowKeepsEverySurvivorsOwnValue()
    {
        IReadOnlyList<FormOption>? captured = null;
        var cut = Render<OptionsEditor>(p => p
            .Add(f => f.Options, ThreeOptions)
            .Add(f => f.OptionsChanged, EventCallback.Factory.Create<IReadOnlyList<FormOption>>(this, list => captured = list)));

        await RemoveButtonFor(cut, 2).ClickAsync(new MouseEventArgs());

        Assert.NotNull(captured);
        Assert.Equal(2, captured!.Count);
        Assert.Equal(["opt-a", "opt-c"], captured.Select(o => o.Value));
        Assert.Equal(["Active", "Closed"], captured.Select(o => o.Label));
    }

    [Fact]
    public async Task MovingARowDownCarriesItsValueAndLabelTogether()
    {
        IReadOnlyList<FormOption>? captured = null;
        var cut = Render<OptionsEditor>(p => p
            .Add(f => f.Options, ThreeOptions)
            .Add(f => f.OptionsChanged, EventCallback.Factory.Create<IReadOnlyList<FormOption>>(this, list => captured = list)));

        // Move "Active" (opt-a, row 1) down one position.
        await MoveDownButtonFor(cut, 1).ClickAsync(new MouseEventArgs());

        Assert.NotNull(captured);
        Assert.Equal(["opt-b", "opt-a", "opt-c"], captured!.Select(o => o.Value));
        Assert.Equal(["Pending", "Active", "Closed"], captured.Select(o => o.Label));
    }

    [Fact]
    public async Task MovingARowUpCarriesItsValueAndLabelTogether()
    {
        IReadOnlyList<FormOption>? captured = null;
        var cut = Render<OptionsEditor>(p => p
            .Add(f => f.Options, ThreeOptions)
            .Add(f => f.OptionsChanged, EventCallback.Factory.Create<IReadOnlyList<FormOption>>(this, list => captured = list)));

        // Move "Closed" (opt-c, row 3) up one position.
        await MoveUpButtonFor(cut, 3).ClickAsync(new MouseEventArgs());

        Assert.NotNull(captured);
        Assert.Equal(["opt-a", "opt-c", "opt-b"], captured!.Select(o => o.Value));
        Assert.Equal(["Active", "Closed", "Pending"], captured.Select(o => o.Label));
    }

    [Fact]
    public void MoveUpIsDisabledOnTheFirstRowAndMoveDownIsDisabledOnTheLastRow()
    {
        var cut = Render<OptionsEditor>(p => p.Add(f => f.Options, ThreeOptions));

        Assert.True(MoveUpButtonFor(cut, 1).HasAttribute("disabled"));
        Assert.False(MoveDownButtonFor(cut, 1).HasAttribute("disabled"));
        Assert.False(MoveUpButtonFor(cut, 3).HasAttribute("disabled"));
        Assert.True(MoveDownButtonFor(cut, 3).HasAttribute("disabled"));
    }

    [Fact]
    public async Task MovingTheFirstRowUpIsANoOpThatNeverCommits()
    {
        var commitCount = 0;
        var cut = Render<OptionsEditor>(p => p
            .Add(f => f.Options, ThreeOptions)
            .Add(f => f.OptionsChanged, EventCallback.Factory.Create<IReadOnlyList<FormOption>>(this, _ => commitCount++)));

        // Disabled buttons never dispatch a click through the DOM in a real browser; this proves
        // the guard also holds if one somehow fired, so a disabled boundary button can never be
        // mistaken for having silently moved anything.
        await MoveUpButtonFor(cut, 1).ClickAsync(new MouseEventArgs());

        Assert.Equal(0, commitCount);
    }

    [Fact]
    public async Task AddingANewOptionMintsAFreshStableValueDistinctFromEveryExistingOneAndFocusesIt()
    {
        IReadOnlyList<FormOption>? captured = null;
        var cut = Render<OptionsEditor>(p => p
            .Add(f => f.Options, TwoOptions)
            .Add(f => f.OptionsChanged, EventCallback.Factory.Create<IReadOnlyList<FormOption>>(this, list => captured = list)));

        await cut.Find("button.bf-options-editor__add-button").ClickAsync(new MouseEventArgs());

        Assert.NotNull(captured);
        Assert.Equal(3, captured!.Count);
        var newValue = captured[2].Value;
        Assert.StartsWith("opt-", newValue, StringComparison.Ordinal);
        Assert.NotEqual("opt-1", newValue);
        Assert.NotEqual("opt-2", newValue);
        Assert.Equal(string.Empty, captured[2].Label);
        JSInterop.VerifyFocusAsyncInvoke(1);
    }

    [Fact]
    public async Task ASubsequentLabelEditOnANewlyAddedOptionDoesNotChangeItsMintedValue()
    {
        IReadOnlyList<FormOption>? afterAdd = null;
        var cut = Render<OptionsEditor>(p => p
            .Add(f => f.Options, TwoOptions)
            .Add(f => f.OptionsChanged, EventCallback.Factory.Create<IReadOnlyList<FormOption>>(this, list => afterAdd = list)));

        await cut.Find("button.bf-options-editor__add-button").ClickAsync(new MouseEventArgs());
        var mintedValue = afterAdd![2].Value;

        // A real PropertiesPanel re-renders this editor with the just-committed Options as the
        // next parameter -- mirror that here rather than reaching into the component's own state.
        IReadOnlyList<FormOption>? afterRelabel = null;
        cut.Render(p => p
            .Add(f => f.Options, afterAdd)
            .Add(f => f.OptionsChanged, EventCallback.Factory.Create<IReadOnlyList<FormOption>>(this, list => afterRelabel = list)));

        await RowInputs(cut)[2].ChangeAsync(new ChangeEventArgs { Value = "Option THREE" });

        Assert.Equal(mintedValue, afterRelabel![2].Value);
        Assert.Equal("Option THREE", afterRelabel[2].Label);
    }

    [Fact]
    public async Task RemovingTheOnlyRemainingRowFocusesTheAddButton()
    {
        var cut = Render<OptionsEditor>(p => p.Add(f => f.Options, [new FormOption { Value = "opt-1", Label = "Option one" }]));

        await RemoveButtonFor(cut, 1).ClickAsync(new MouseEventArgs());

        Assert.Empty(RowInputs(cut));
        JSInterop.VerifyFocusAsyncInvoke(1);
    }

    [Fact]
    public async Task EachOperationRaisesExactlyOneCommit()
    {
        var commitCount = 0;
        var cut = Render<OptionsEditor>(p => p
            .Add(f => f.Options, ThreeOptions)
            .Add(f => f.OptionsChanged, EventCallback.Factory.Create<IReadOnlyList<FormOption>>(this, _ => commitCount++)));

        await RowInputs(cut)[0].ChangeAsync(new ChangeEventArgs { Value = "Active edited" });
        Assert.Equal(1, commitCount);

        await MoveDownButtonFor(cut, 1).ClickAsync(new MouseEventArgs());
        Assert.Equal(2, commitCount);

        await RemoveButtonFor(cut, 1).ClickAsync(new MouseEventArgs());
        Assert.Equal(3, commitCount);

        await cut.Find("button.bf-options-editor__add-button").ClickAsync(new MouseEventArgs());
        Assert.Equal(4, commitCount);
    }

    [Fact]
    public async Task TypingDoesNotCommitOnlyBlurDoes()
    {
        var commitCount = 0;
        var cut = Render<OptionsEditor>(p => p
            .Add(f => f.Options, TwoOptions)
            .Add(f => f.OptionsChanged, EventCallback.Factory.Create<IReadOnlyList<FormOption>>(this, _ => commitCount++)));

        // The row input binds only "onchange" (blur) -- confirmed by the rendered markup carrying
        // no "oninput" wiring at all, so bUnit itself refuses to dispatch a keystroke-level event
        // here. That absence is the proof: there is no path from a keystroke to a commit.
        var input = RowInputs(cut)[0];
        await Assert.ThrowsAsync<MissingEventHandlerException>(
            () => input.InputAsync(new ChangeEventArgs { Value = "Typing..." }));
        Assert.Equal(0, commitCount);

        await input.ChangeAsync(new ChangeEventArgs { Value = "Typing..." });
        Assert.Equal(1, commitCount);
    }

    [Fact]
    public void EachRowInputHasAProgrammaticLabelNamingItsOwnOrdinal()
    {
        var cut = Render<OptionsEditor>(p => p.Add(f => f.Options, TwoOptions));

        var inputs = RowInputs(cut);
        var firstLabel = cut.Find($"label[for='{inputs[0].GetAttribute("id")}']");
        var secondLabel = cut.Find($"label[for='{inputs[1].GetAttribute("id")}']");

        Assert.Equal("Option 1 label", firstLabel.TextContent);
        Assert.Equal("Option 2 label", secondLabel.TextContent);
    }

    [Fact]
    public void EveryRemoveAndMoveButtonIsARealKeyboardOperableButtonWithAnAccessibleName()
    {
        var cut = Render<OptionsEditor>(p => p.Add(f => f.Options, ThreeOptions));

        foreach (var button in cut.FindAll("div.bf-options-editor__row-actions button"))
        {
            Assert.Equal("button", button.TagName, ignoreCase: true);
            Assert.Equal("button", button.GetAttribute("type"));
            Assert.False(string.IsNullOrWhiteSpace(button.GetAttribute("aria-label")));
        }
    }
}
