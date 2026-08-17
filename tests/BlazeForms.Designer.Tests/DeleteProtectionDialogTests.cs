using BlazeForms.Definitions;
using BlazeForms.Delete;
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Linting;
using BlazeForms.Versioning;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="DeleteProtectionDialog"/> in isolation (Phase 7, PRD §4.1): it names every
/// live reference to the node it was opened for -- a visibility rule and a validation rule's own
/// target and expression, one line each -- confirms a delete through the exact same
/// <see cref="DesignerEditContext.DeleteNode"/> an unreferenced delete uses, cancels (<c>Esc</c>
/// and the Cancel button) without touching the draft, and satisfies its own
/// <c>role="dialog" aria-modal="true"</c>/focus-trap contract. Coverage of the <c>Delete</c>-key
/// trigger and the post-close focus destination lives in <c>DesignerCanvasTests</c>, the same
/// split <c>MoveToPositionDialogTests</c> documents for its own dialog.
/// </summary>
public sealed class DeleteProtectionDialogTests : DesignerTestContext
{
    private static DesignerEditContext CreateContext(FormDefinition definition, IFormDefinitionStore? store = null) =>
        new(FormLifecycle.CreateDraft(definition), store ?? new InMemoryFormDefinitionStore());

    [Fact]
    public async Task RendersAsAFocusLabelledModalDialogNamingEveryReference()
    {
        await using var context = CreateContext(DesignerTestFixtures.ReferencedFieldDefinition("form-1"));
        var cut = Render<DeleteProtectionDialog>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-referenced"));

        var dialog = cut.Find("div.bf-delete-dialog");
        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.Equal(cut.Find("h2").Id, dialog.GetAttribute("aria-labelledby"));

        var lines = cut.FindAll("ul.bf-delete-dialog__list li").Select(li => li.TextContent).ToArray();
        Assert.Equal(3, lines.Length);
        Assert.Contains(lines, line => line.Contains("Dependent field", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Enter a value for 'Referenced field'.", StringComparison.Ordinal));
    }

    /// <summary>
    /// A field named only by a calc node's own calculation (<see cref="ReferenceKind.Calculation"/>)
    /// is described by its own dedicated phrase — "used in '&lt;calc&gt;''s calculation" — not
    /// generically folded into the visibility-rule line (calc-engine-plan.md, Increment C).
    /// </summary>
    [Fact]
    public async Task RendersACalculationReferenceLine()
    {
        await using var context = CreateContext(DesignerTestFixtures.CalcReferencedFieldDefinition("form-1"));
        var cut = Render<DeleteProtectionDialog>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-referenced"));

        var lines = cut.FindAll("ul.bf-delete-dialog__list li").Select(li => li.TextContent).ToArray();
        Assert.Single(lines);

        // Pins the exact rendered text -- a single apostrophe, never the doubled "Total''s"
        // string.Format leaves behind if the resx literally quotes '{0}''s (code review fix #3).
        Assert.Equal("Used in 'Total's calculation.", lines[0]);
    }

    [Fact]
    public async Task DeleteAnywayDeletesTheNodeAndRaisesOnClosed()
    {
        await using var context = CreateContext(DesignerTestFixtures.ReferencedFieldDefinition("form-1"));
        var closed = false;
        var cut = Render<DeleteProtectionDialog>(p => p
            .Add(d => d.EditContext, context)
            .Add(d => d.NodeId, "node-referenced")
            .Add(d => d.OnClosed, () => closed = true));

        await cut.Find("button.bf-delete-dialog__button--danger").ClickAsync(new MouseEventArgs());

        Assert.Null(context.Draft.Definition.FindNode("node-referenced"));
        Assert.True(closed);

        // The dangling reference the delete left behind is exactly what FR-03 now blocks on.
        var lint = FormLinter.CreateDefault().Lint(context.Draft.Definition);
        Assert.Contains(lint, r => r.RuleId == LintRuleIds.Fr03);
    }

    /// <summary>
    /// Deleting a repeating group aggregates references to the group itself AND to every one of
    /// its own children (repeating-groups-plan.md, Increment C) -- a group deletes its whole
    /// subtree, so a reference to any child is exactly as much a live reference as one to the
    /// group's own id.
    /// </summary>
    [Fact]
    public async Task DeletingARepeatingGroupNamesReferencesToItsOwnChildrenToo()
    {
        await using var context = CreateContext(DesignerTestFixtures.RepeatingGroupWithReferencedChildDefinition("form-1"));
        var cut = Render<DeleteProtectionDialog>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "group-1"));

        var lines = cut.FindAll("ul.bf-delete-dialog__list li").Select(li => li.TextContent).ToArray();
        Assert.Single(lines);
        Assert.Contains(lines, line => line.Contains("Date of birth", StringComparison.Ordinal));
    }

    /// <summary>
    /// Deleting a group's own child works exactly as today -- <c>ExpressionDependencyAnalysis.ReferencesTo</c>
    /// already finds a sibling's own reference by walking <see cref="FormNode.Children"/>
    /// (<c>EnumerateNodes</c> descends), so this is unchanged by Increment C, pinned here so a
    /// future regression in the group-vs-child dispatch above cannot silently break it.
    /// </summary>
    /// <summary>
    /// Deleting a repeating group lists each referencing site once, even when a single rule names
    /// two of the group's own children at the same time (repeating-groups-plan.md, Increment C).
    /// "child-c"'s own <see cref="FormNode.VisibleWhen"/> references both "child-a" and "child-b",
    /// so aggregating <c>ReferencesTo</c> per deleted id would surface it twice without the dedup
    /// -- the warning must name that one broken rule once, not once per member it reads.
    /// </summary>
    [Fact]
    public async Task DeletingAGroupWhoseOneRuleNamesTwoChildrenListsThatReferenceOnce()
    {
        await using var context = CreateContext(DesignerTestFixtures.RepeatingGroupWithRuleReferencingTwoChildrenDefinition("form-1"));
        var cut = Render<DeleteProtectionDialog>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "group-1"));

        var lines = cut.FindAll("ul.bf-delete-dialog__list li").Select(li => li.TextContent).ToArray();
        Assert.Single(lines);
        Assert.Contains(lines, line => line.Contains("Notes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeletingAGroupsOwnChildNamesOnlyThatChildsOwnReferences()
    {
        await using var context = CreateContext(DesignerTestFixtures.RepeatingGroupWithReferencedChildDefinition("form-1"));
        var cut = Render<DeleteProtectionDialog>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "child-a"));

        var lines = cut.FindAll("ul.bf-delete-dialog__list li").Select(li => li.TextContent).ToArray();
        Assert.Single(lines);
        Assert.Contains(lines, line => line.Contains("Date of birth", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EscCancelsWithoutTouchingTheDraftAndRaisesOnClosed()
    {
        await using var context = CreateContext(DesignerTestFixtures.ReferencedFieldDefinition("form-1"));
        var closed = false;
        var cut = Render<DeleteProtectionDialog>(p => p
            .Add(d => d.EditContext, context)
            .Add(d => d.NodeId, "node-referenced")
            .Add(d => d.OnClosed, () => closed = true));

        await cut.Find("div.bf-delete-dialog").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.NotNull(context.Draft.Definition.FindNode("node-referenced"));
        Assert.True(closed);
    }

    [Fact]
    public async Task CancelButtonCancelsWithoutTouchingTheDraft()
    {
        await using var context = CreateContext(DesignerTestFixtures.ReferencedFieldDefinition("form-1"));
        var closed = false;
        var cut = Render<DeleteProtectionDialog>(p => p
            .Add(d => d.EditContext, context)
            .Add(d => d.NodeId, "node-referenced")
            .Add(d => d.OnClosed, () => closed = true));

        // The Cancel button is the second of the dialog's two buttons -- the danger button is first.
        await cut.FindAll("button.bf-delete-dialog__button")[1].ClickAsync(new MouseEventArgs());

        Assert.NotNull(context.Draft.Definition.FindNode("node-referenced"));
        Assert.True(closed);
    }

    [Fact]
    public async Task TheFocusTrapModuleIsImportedAndFocusDefaultsToCancelNotTheDangerButton()
    {
        var module = JSInterop.SetupModule(DeleteProtectionDialog.ModulePath);
        await using var context = CreateContext(DesignerTestFixtures.ReferencedFieldDefinition("form-1"));
        var cut = Render<DeleteProtectionDialog>(p => p.Add(d => d.EditContext, context).Add(d => d.NodeId, "node-referenced"));

        cut.WaitForAssertion(() => Assert.True(cut.Instance.HasImportedModule));
        module.VerifyInvoke("attachFocusTrap");
        JSInterop.VerifyFocusAsyncInvoke();

        await cut.Instance.DisposeAsync();

        Assert.False(cut.Instance.HasImportedModule);
    }
}
