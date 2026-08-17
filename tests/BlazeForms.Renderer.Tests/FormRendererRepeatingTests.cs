using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using BlazeForms.Components;
using BlazeForms.Fields;
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Serialization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers <see cref="FormRenderer"/>'s Increment B wiring of a fillable repeating group
/// (repeating-groups-plan.md, D-3): add/remove/reorder mutating the group's
/// <see cref="RepeatingRows"/> value, the keyboard/focus model, the shared row-operation live
/// region, per-row validation (including the group-level row-count rule), within-row visibility,
/// per-row calc, render discipline, draft round-tripping, and capture-at-submit.
/// </summary>
public sealed class FormRendererRepeatingTests : RendererTestContext
{
    public FormRendererRepeatingTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private static string AnnouncerText(IRenderedComponent<FormRenderer> cut) =>
        cut.Find("div.bf-repeating-announcer").TextContent;

    [Fact]
    public void AddAppendsARowUpToMaxRowsAndFocusesTheFirstControlOfTheNewRow()
    {
        var module = JSInterop.SetupModule(RepeatingGroup.ModulePath);
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RepeatingDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        // MinRows = 1 seeds one row on first render.
        Assert.Single(cut.FindAll(".bf-repeating-group__row"));

        cut.Find(".bf-repeating-group__add-button").Click();

        var rows = cut.FindAll(".bf-repeating-group__row");
        Assert.Equal(2, rows.Count);
        Assert.Equal("Sibling 2", rows[1].QuerySelector("legend")!.TextContent);

        Assert.Contains("Sibling 2 added.", AnnouncerText(cut), StringComparison.Ordinal);
        Assert.Contains("2 of 3.", AnnouncerText(cut), StringComparison.Ordinal);

        // Focus moves to the new row's own first control -- reached through the collocated JS
        // module, keyed to that exact row's own container id, since there is no ElementReference
        // for a field rendered by an arbitrary, host-resolvable component.
        var invocation = module.VerifyInvoke("focusFirstControlIn");
        Assert.Equal(rows[1].Id, invocation.Arguments[0]);
    }

    [Fact]
    public void AddAtMaxRowsIsANoOpAndAnnouncesWhyRatherThanAddingARow()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RepeatingDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        cut.Find(".bf-repeating-group__add-button").Click(); // 1 -> 2
        cut.Find(".bf-repeating-group__add-button").Click(); // 2 -> 3 (MaxRows)
        Assert.Equal(3, cut.FindAll(".bf-repeating-group__row").Count);
        Assert.Equal("true", cut.Find(".bf-repeating-group__add-button").GetAttribute("aria-disabled"));

        cut.Find(".bf-repeating-group__add-button").Click(); // Blocked.

        Assert.Equal(3, cut.FindAll(".bf-repeating-group__row").Count);
        Assert.Contains("Cannot add another Sibling", AnnouncerText(cut), StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveDeletesTheRowAndFocusesTheNextRowsRemoveButton()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RepeatingDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        cut.Find(".bf-repeating-group__add-button").Click(); // 1 -> 2 (both rows removable now)
        var firstRowId = cut.FindAll(".bf-repeating-group__row")[0].Id;

        cut.FindAll(".bf-repeating-group__remove-button")[0].Click();

        var remainingRows = cut.FindAll(".bf-repeating-group__row");
        Assert.Single(remainingRows);
        Assert.NotEqual(firstRowId, remainingRows[0].Id);

        Assert.Contains("Sibling 1 removed.", AnnouncerText(cut), StringComparison.Ordinal);
        Assert.Contains("1 of 3.", AnnouncerText(cut), StringComparison.Ordinal);

        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void RemovingTheLastRowFocusesThePreviousRowsRemoveButton()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RepeatingDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        cut.Find(".bf-repeating-group__add-button").Click(); // 1 -> 2

        cut.FindAll(".bf-repeating-group__remove-button")[1].Click(); // Remove the LAST row.

        Assert.Single(cut.FindAll(".bf-repeating-group__row"));
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void RemoveDownToMinRowsIsANoOpAndAnnouncesWhyRatherThanRemoving()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RepeatingDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        // MinRows = 1, and exactly one row is seeded -- Remove must already be blocked.
        Assert.Equal("true", cut.Find(".bf-repeating-group__remove-button").GetAttribute("aria-disabled"));

        cut.Find(".bf-repeating-group__remove-button").Click();

        Assert.Single(cut.FindAll(".bf-repeating-group__row"));
        Assert.Contains("Cannot remove this Sibling", AnnouncerText(cut), StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingTheOnlyRemainingRowFocusesTheAddButton()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RepeatingWithinRowVisibilityDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        // This fixture's MinRows is unset (0), so the seeded start is empty; add one row, then
        // remove it -- with no sibling row to fall back to, focus must land on Add.
        cut.Find(".bf-repeating-group__add-button").Click();
        Assert.Single(cut.FindAll(".bf-repeating-group__row"));

        cut.Find(".bf-repeating-group__remove-button").Click();

        Assert.Empty(cut.FindAll(".bf-repeating-group__row"));
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void MoveDownReordersTheRowAndAnnouncesItsNewPosition()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RepeatingDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        cut.Find(".bf-repeating-group__add-button").Click(); // 1 -> 2
        var rowIdsBefore = cut.FindAll(".bf-repeating-group__row").Select(e => e.Id).ToList();

        // Move down on the FIRST row's move-down button (index 1 of the pair: up, down).
        cut.FindAll(".bf-repeating-group__move-button")[1].Click();

        var rowIdsAfter = cut.FindAll(".bf-repeating-group__row").Select(e => e.Id).ToList();
        Assert.Equal([rowIdsBefore[1], rowIdsBefore[0]], rowIdsAfter);
        Assert.Contains("moved to position 2 of 2.", AnnouncerText(cut), StringComparison.Ordinal);
    }

    [Fact]
    public void MoveUpOnTheFirstRowNeverReordersButStillAnnouncesWhy()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RepeatingDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        cut.Find(".bf-repeating-group__add-button").Click(); // 1 -> 2
        var rowIdsBefore = cut.FindAll(".bf-repeating-group__row").Select(e => e.Id).ToList();

        cut.FindAll(".bf-repeating-group__move-button")[0].Click(); // Move up on the first row.

        var rowIdsAfter = cut.FindAll(".bf-repeating-group__row").Select(e => e.Id).ToList();
        Assert.Equal(rowIdsBefore, rowIdsAfter);
        // The move itself is a no-op (RepeatingRows.MoveRow's own bounds rule), but a boundary
        // press still announces why -- screen-reader parity with a blocked Add/Remove, rather than
        // leaving the press silently unconfirmed.
        Assert.Contains("cannot move up", AnnouncerText(cut), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TypingInOneRowsFieldDoesNotReRenderASiblingRowsField()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RepeatingDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        cut.Find(".bf-repeating-group__add-button").Click(); // 1 -> 2

        var fields = cut.FindComponents<TextField>();
        Assert.Equal(2, fields.Count);
        var secondRowField = fields[1];
        var rendersBefore = secondRowField.RenderCount;

        cut.FindAll("input[type='text']")[0].Input("Ada");

        Assert.Equal(rendersBefore, secondRowField.RenderCount);
    }

    [Fact]
    public void ARequiredChildLeftBlankBlocksSubmitWithARemedyAnchoredToThatRowsControl()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RepeatingDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        cut.Find(".bf-step-nav__button--primary").Click(); // Submit -- "sibling-name" is blank.

        var summary = cut.Find("[role='alert']");
        var link = summary.QuerySelector("a")!;
        Assert.Equal("Enter a value for 'Sibling name'.", link.TextContent);

        var input = cut.Find("input[type='text']");
        Assert.Equal("#" + input.GetAttribute("id"), link.GetAttribute("href"));
        Assert.NotNull(input.ParentElement!.QuerySelector(".bf-field__error"));
    }

    [Fact]
    public async Task FewerThanMinRowsBlocksSubmitWithTheGroupLevelRemedyMessage()
    {
        // A resumed draft can carry fewer rows than the group's *current* MinRows (an author
        // raised it after the draft was saved) -- the seeded MinRows rows never survive a resume,
        // since LoadDraftAsync unconditionally overwrites every key the draft carries.
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RepeatingDefinition);
        var store = new InMemoryFormDraftStore();
        var key = new FormDraftKey(version.FormId, version.Version, "resp-1");
        await store.SaveAsync(new FormDraft
        {
            Key = key,
            StartedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Values = FormValues.ToJsonValues(new Dictionary<string, object?>(StringComparer.Ordinal) { ["siblings"] = RepeatingRows.Empty }),
            CurrentPageIndex = 0,
        });
        Services.AddSingleton<IFormDraftStore>(store);

        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.RespondentKey, "resp-1"));

        Assert.Empty(cut.FindAll(".bf-repeating-group__row"));

        await cut.Find(".bf-step-nav__button--primary").ClickAsync(new());

        var summary = cut.Find("[role='alert']");
        Assert.Contains("Add at least 1 Sibling entries.", summary.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void WithinRowVisibilityHidesAChildInOneRowButNotAnother()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RepeatingWithinRowVisibilityDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        cut.Find(".bf-repeating-group__add-button").Click();
        Assert.Empty(cut.FindAll("textarea")); // "wants-notes" starts unchecked -- hidden.

        cut.Find("input[type='checkbox']").Change(true);
        Assert.NotEmpty(cut.FindAll("textarea"));

        cut.Find("input[type='checkbox']").Change(false);
        Assert.Empty(cut.FindAll("textarea"));
    }

    [Fact]
    public async Task SubmitCapturesAVisibleGroupsRowsAndOmitsAHiddenGroupAndHiddenInRowChildren()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RepeatingWithinRowVisibilityDefinition);
        FormSubmissionEnvelope? captured = null;
        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.OnSubmitted, (FormSubmissionEnvelope e) => captured = e));

        await cut.Find(".bf-repeating-group__add-button").ClickAsync(new());
        await cut.FindAll("input[type='text']")[0].InputAsync("Ada"); // Row 1: no notes.

        await cut.Find(".bf-repeating-group__add-button").ClickAsync(new());
        var nameInputs = cut.FindAll("input[type='text']");
        await nameInputs[1].InputAsync("Grace"); // Row 2: notes.
        await cut.FindAll("input[type='checkbox']")[1].ChangeAsync(true);
        await cut.FindAll("textarea")[0].InputAsync("Loves math");

        await cut.Find(".bf-step-nav__button--primary").ClickAsync(new());

        Assert.NotNull(captured);
        Assert.True(captured.Values.TryGetValue("siblings", out var siblingsJson));
        Assert.Equal(JsonValueKind.Array, siblingsJson.ValueKind);
        var rows = siblingsJson.EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);

        var firstRowValues = rows[0].GetProperty("values");
        Assert.Equal("Ada", firstRowValues.GetProperty("sibling-name").GetString());
        Assert.False(firstRowValues.TryGetProperty("sibling-notes", out _), "A within-row hidden child must be absent, not null.");

        var secondRowValues = rows[1].GetProperty("values");
        Assert.Equal("Grace", secondRowValues.GetProperty("sibling-name").GetString());
        Assert.Equal("Loves math", secondRowValues.GetProperty("sibling-notes").GetString());
    }

    [Fact]
    public async Task AHiddenGroupIsAbsentFromTheEnvelope()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RepeatingHiddenByOuterVisibilityDefinition);
        FormSubmissionEnvelope? captured = null;
        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.OnSubmitted, (FormSubmissionEnvelope e) => captured = e));

        // "has-siblings" is never checked -- the group (and its RepeatingGroup component) never
        // renders at all.
        Assert.Empty(cut.FindAll(".bf-repeating-group"));

        await cut.Find(".bf-step-nav__button--primary").ClickAsync(new());

        Assert.NotNull(captured);
        Assert.False(captured.Values.ContainsKey("siblings"), "A hidden group's value must be absent, not null.");
    }

    [Fact]
    public void PerRowCalcRecomputesAndDisplaysInEachRowsOwnCalcField()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RepeatingDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        cut.Find("input[type='number']").Input("5");

        var output = cut.Find("output");
        Assert.Equal("6", output.TextContent);
    }

    [Fact]
    public async Task DraftSaveAndResumeRoundTripsRowsIncludingADateOnlyChild()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RepeatingWithDateChildDefinition);
        var store = new InMemoryFormDraftStore();
        Services.AddSingleton<IFormDraftStore>(store);

        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.RespondentKey, "resp-1"));

        await cut.Find(".bf-repeating-group__add-button").ClickAsync(new());
        await cut.Find("input[type='text']").InputAsync("Ada");
        await cut.Find("input[type='date']").ChangeAsync("2020-05-01");
        await cut.Find("input[type='date']").BlurAsync(new());

        var savedDraft = (await store.LoadAsync(new FormDraftKey(version.FormId, version.Version, "resp-1")))!;
        Assert.True(savedDraft.Values.TryGetValue("siblings", out var savedRows));
        Assert.Equal(JsonValueKind.Array, savedRows.ValueKind);

        // A fresh renderer instance resumes the same draft key -- proving the round trip through
        // the store, not just an in-memory value that never left this component.
        var resumed = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.RespondentKey, "resp-1"));

        Assert.Single(resumed.FindAll(".bf-repeating-group__row"));
        var nameInput = resumed.Find("input[type='text']");
        Assert.Equal("Ada", nameInput.GetAttribute("value") ?? nameInput.TextContent);
        Assert.Equal("2020-05-01", resumed.Find("input[type='date']").GetAttribute("value"));
    }

    /// <summary>
    /// Regression for the settled-vs-raw divergence: a row child's own <c>VisibleWhen</c> names
    /// an OUTER field that is itself conditionally hidden. Filling both, then hiding the outer
    /// field again, must hide the row child too -- agreeing with what capture will actually drop
    /// -- rather than leaving it stranded visible-and-required against the outer field's now-stale
    /// raw answer (which would both fail to render what capture omits, and block submit on a
    /// requirement capture would never have enforced).
    /// </summary>
    [Fact]
    public async Task ARowChildVisibleWhenAnOuterFieldHidesAgreesWithCaptureRatherThanBlockingOnStaleData()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RepeatingChildVisibleWhenOuterFieldDefinition);
        FormSubmissionEnvelope? captured = null;
        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.OnSubmitted, (FormSubmissionEnvelope e) => captured = e));

        await cut.Find("input[type='checkbox']").ChangeAsync(true); // show-detail -> true.
        await cut.Find("input[type='text']").InputAsync("hello"); // outer-detail.

        await cut.Find(".bf-repeating-group__add-button").ClickAsync(new());

        // "confirm" is now visible within the row, since "outer-detail" is non-blank.
        Assert.Equal(2, cut.FindAll("input[type='text']").Count);

        await cut.Find("input[type='checkbox']").ChangeAsync(false); // show-detail -> false.

        // "outer-detail" hides at the top level, and its own row child ("confirm") -- whose
        // VisibleWhen reads "outer-detail" -- must agree and hide too, not keep reading the
        // now-stale raw answer that is still sitting in _values.
        Assert.Empty(cut.FindAll("input[type='text']"));

        await cut.Find(".bf-step-nav__button--primary").ClickAsync(new());

        // Never blocked: the bug would leave "confirm" visible and Required against the stale
        // answer, failing validation right here.
        Assert.NotNull(captured);
        Assert.Empty(cut.FindAll("[role='alert']"));

        Assert.True(captured.Values.TryGetValue("items", out var itemsJson));
        var row = itemsJson.EnumerateArray().Single().GetProperty("values");
        Assert.False(row.TryGetProperty("confirm", out _), "A row child hidden via a since-hidden outer field must be absent, not stale.");
    }

    /// <summary>
    /// A host-registered override for <see cref="Definitions.NodeType.Repeating"/> renders through
    /// its own <c>DynamicComponent</c>, at its own DOM -- never <see cref="Components.RepeatingGroup"/>
    /// at all -- so it must own its own per-child validation entirely: <see cref="FormRenderer"/>
    /// must never synthesize a composite-key error a host's markup carries no anchor for, which
    /// would otherwise block submit invisibly and leave a dead error-summary link. The group-level
    /// row-count rule, which the host's single <see cref="Fields.FormFieldBase.Error"/> parameter
    /// does carry, must still work.
    /// </summary>
    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Instantiated by the renderer's DynamicComponent through Type identity, not directly by this test.")]
    private sealed class HostRepeatingField : FormFieldBase
    {
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "data-host-repeating-field", "true");
            builder.AddAttribute(2, "id", FieldId);

            if (!string.IsNullOrEmpty(Error))
            {
                builder.AddAttribute(3, "data-error", Error);
            }

            builder.CloseElement();
        }
    }

    private sealed class HostRepeatingFieldRegistry : IFieldComponentRegistry
    {
        public bool TryGetComponentType(Definitions.NodeType nodeType, out Type? componentType)
        {
            if (nodeType == Definitions.NodeType.Repeating)
            {
                componentType = typeof(HostRepeatingField);
                return true;
            }

            componentType = null;
            return false;
        }
    }

    [Fact]
    public async Task AHostRegisteredRepeatingOverrideNeverGetsCompositeChildErrorsAndSubmitsCleanly()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RepeatingDefinition);
        FormSubmissionEnvelope? captured = null;
        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.FieldComponents, new HostRepeatingFieldRegistry())
            .Add(f => f.OnSubmitted, (FormSubmissionEnvelope e) => captured = e));

        // The internal RepeatingGroup never renders at all -- the host's own component does,
        // carrying the seeded MinRows=1 row's whole RepeatingRows value with no per-child errors
        // of its own (the host never validates it in this test, deliberately).
        Assert.Empty(cut.FindAll(".bf-repeating-group"));
        Assert.NotNull(cut.Find("[data-host-repeating-field]"));

        await cut.Find(".bf-step-nav__button--primary").ClickAsync(new());

        // "sibling-name" is Required and blank inside the seeded row -- with the internal
        // RepeatingGroup this would block submit via a composite-key error; a host override owns
        // that validation entirely, so it must never happen here, and submit must succeed with no
        // dead error-summary anchors.
        Assert.NotNull(captured);
        Assert.Empty(cut.FindAll("[role='alert']"));
    }

    [Fact]
    public async Task AHostRegisteredRepeatingOverrideStillEnforcesTheGroupLevelRowCountRule()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RepeatingDefinition);
        var store = new InMemoryFormDraftStore();
        var key = new FormDraftKey(version.FormId, version.Version, "resp-1");
        await store.SaveAsync(new FormDraft
        {
            Key = key,
            StartedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Values = FormValues.ToJsonValues(new Dictionary<string, object?>(StringComparer.Ordinal) { ["siblings"] = RepeatingRows.Empty }),
            CurrentPageIndex = 0,
        });
        Services.AddSingleton<IFormDraftStore>(store);

        var cut = Render<FormRenderer>(p => p
            .Add(f => f.Version, version)
            .Add(f => f.RespondentKey, "resp-1")
            .Add(f => f.FieldComponents, new HostRepeatingFieldRegistry()));

        await cut.Find(".bf-step-nav__button--primary").ClickAsync(new());

        // MinRows = 1, and the resumed draft carries zero rows -- the row-count rule is the one
        // piece of validation FormRenderer itself still owns even behind a host override, so it
        // must still block submit and surface through the host's own single Error parameter.
        var hostField = cut.Find("[data-host-repeating-field]");
        Assert.Equal("Add at least 1 Sibling entries.", hostField.GetAttribute("data-error"));
    }

    [Fact]
    public async Task MoveBlockedAtTheFirstRowAnnouncesRatherThanStayingSilent()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RepeatingDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        await cut.FindAll(".bf-repeating-group__move-button")[0].ClickAsync(new()); // Move up, only row.

        Assert.Contains("cannot move up", AnnouncerText(cut), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sibling 1", AnnouncerText(cut), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MoveBlockedAtTheLastRowAnnouncesRatherThanStayingSilent()
    {
        var version = FormRendererTestFixtures.ToPublishedVersion(FormRendererTestFixtures.RepeatingDefinition);
        var cut = Render<FormRenderer>(p => p.Add(f => f.Version, version));

        var moveButtons = cut.FindAll(".bf-repeating-group__move-button");
        await moveButtons[1].ClickAsync(new()); // Move down, only row.

        Assert.Contains("cannot move down", AnnouncerText(cut), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sibling 1", AnnouncerText(cut), StringComparison.Ordinal);
    }
}
