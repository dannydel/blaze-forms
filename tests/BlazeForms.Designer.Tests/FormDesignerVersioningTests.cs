using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Serialization;
using BlazeForms.Versioning;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="FormDesigner"/>'s Phase 8 wiring (PRD §7): the toolbar's Publish and
/// Version-history buttons open <see cref="PublishDialog"/> and <see cref="VersionHistory"/>
/// respectively, both restore focus to their own opener once closed, a successful publish tears
/// down and reloads the shell's <see cref="DesignerEditContext"/> so it reflects the
/// now-consumed draft, and "revise as draft" swaps the shell's whole editing session onto a new
/// draft without ever touching the version it was revised from.
/// </summary>
public sealed class FormDesignerVersioningTests : DesignerTestContext
{
    [Fact]
    public async Task ToolbarShowsPublishAndVersionHistoryButtonsOnlyOnceTheDraftHasLoaded()
    {
        var store = new InMemoryFormDefinitionStore();
        Services.AddSingleton<IFormDefinitionStore>(store);

        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, "form-1"));

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll("button.bf-designer__publish-button"));
            Assert.NotEmpty(cut.FindAll("button.bf-designer__version-history-button"));
        });
    }

    [Fact]
    public async Task PublishButtonOpensThePublishDialogAndEscRestoresFocusToThePublishButton()
    {
        var store = new InMemoryFormDefinitionStore();
        const string formId = "form-1";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition(formId)));
        Services.AddSingleton<IFormDefinitionStore>(store);
        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button.bf-designer__publish-button")));

        await cut.Find("button.bf-designer__publish-button").ClickAsync(new MouseEventArgs());
        Assert.NotEmpty(cut.FindAll("div.bf-publish-dialog"));

        await cut.Find("div.bf-publish-dialog").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll("div.bf-publish-dialog"));
        // Once for the dialog's own initial Cancel-button focus, again once ClosePublishDialog's
        // restore flag is consumed on the render after the dialog has actually left the DOM --
        // the same "count both the dialog's own initial focus and the trigger's restore" shape
        // FormDesignerTests' own keyboard-help Esc test uses for itself.
        JSInterop.VerifyFocusAsyncInvoke(2);
    }

    [Fact]
    public async Task APublishThroughTheFullShellTearsDownAndReloadsTheEditContextAndBubblesOnPublished()
    {
        var store = new RecordingFormDefinitionStore();
        const string formId = "form-1";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition(formId)));
        Services.AddSingleton<IFormDefinitionStore>(store);
        FormVersion? raised = null;
        var cut = Render<FormDesigner>(p => p
            .Add(f => f.FormId, formId)
            .Add(f => f.OnPublished, (FormVersion v) => raised = v));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Instance.EditContext));
        var originalEditContext = cut.Instance.EditContext!;

        await cut.Find("button.bf-designer__publish-button").ClickAsync(new MouseEventArgs());
        await cut.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Initial publish." });
        await cut.Find("button.bf-publish-dialog__button--primary").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, store.PublishCallCount);
        Assert.NotNull(raised);
        Assert.Equal(1, raised!.Version);
        Assert.Empty(cut.FindAll("div.bf-publish-dialog"));

        // The shell's own EditContext is a brand-new instance over the store-miss path's revised
        // draft -- never the same one PublishDialog just published, since that draft no longer
        // exists (PublishAsync consumes it).
        cut.WaitForAssertion(() => Assert.NotSame(originalEditContext, cut.Instance.EditContext));
        Assert.Equal(FormLifecycleState.Draft, cut.Instance.EditContext!.Draft.State);
        Assert.Equal(0, cut.Instance.EditContext.Draft.Version);
    }

    [Fact]
    public async Task VersionHistoryButtonOpensThePanelAndItsCloseButtonRestoresFocus()
    {
        var store = new InMemoryFormDefinitionStore();
        const string formId = "form-1";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition(formId)));
        Services.AddSingleton<IFormDefinitionStore>(store);
        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button.bf-designer__version-history-button")));

        await cut.Find("button.bf-designer__version-history-button").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("div.bf-version-history")));

        await cut.Find("button.bf-version-history__close").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll("div.bf-version-history"));
        // Once for the panel's own initial Close-button focus (VersionHistory's own
        // OnAfterRenderAsync), again once CloseVersionHistory's restore flag is consumed on the
        // render after the panel has actually left the DOM -- the same "count both" shape every
        // other dialog-close focus test in this project uses.
        JSInterop.VerifyFocusAsyncInvoke(2);
    }

    [Fact]
    public async Task ReviseAsDraftFromVersionHistorySwapsTheEditContextOntoTheNewDraftWithoutMutatingTheOldVersion()
    {
        var store = new InMemoryFormDefinitionStore();
        const string formId = "form-1";
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition(formId)));
        await store.PublishAsync(formId, "First cut.", "author-1");
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.TwoSectionDefinition(formId)));
        await store.PublishAsync(formId, "Added a section.", "author-2");
        Services.AddSingleton<IFormDefinitionStore>(store);
        var originalVersion1 = await store.GetVersionAsync(formId, 1);
        // Captured before the revise, not just held as a reference -- InMemoryFormDefinitionStore
        // hands back the SAME instance on every read, so comparing that same reference to itself
        // after the fact would pass even if something mutated it in place. Serializing pins the
        // actual JSON shape, making the immutability assertion below self-enforcing rather than
        // reference-dependent (AGENTS.md invariant #3).
        var originalVersion1Json = FormJson.SerializeDefinition(originalVersion1!.Definition);

        var cut = Render<FormDesigner>(p => p.Add(f => f.FormId, formId));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Instance.EditContext));
        var originalEditContext = cut.Instance.EditContext!;

        await cut.Find("button.bf-designer__version-history-button").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("div.bf-version-history")));

        // Newest first -- version 2's own row is first; version 1's "Revise as draft" is the
        // second such button in document order.
        await cut.FindAll("button").Where(b => b.TextContent == "Revise as draft").ElementAt(1).ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll("div.bf-version-history"));
        cut.WaitForAssertion(() => Assert.NotSame(originalEditContext, cut.Instance.EditContext));
        var newContext = cut.Instance.EditContext!;
        Assert.Equal(FormLifecycleState.Draft, newContext.Draft.State);
        Assert.Equal(0, newContext.Draft.Version);
        Assert.Equal(DesignerTestFixtures.OneFieldDefinition(formId).Name, newContext.Draft.Definition.Name);

        // The version this draft was revised from is completely untouched (AGENTS.md invariant
        // #3) -- same serialized shape, same state.
        var stillOriginalVersion1 = await store.GetVersionAsync(formId, 1);
        Assert.Equal(originalVersion1, stillOriginalVersion1);
        Assert.Equal(originalVersion1Json, FormJson.SerializeDefinition(stillOriginalVersion1!.Definition));
        Assert.Equal(FormLifecycleState.Published, stillOriginalVersion1.State);
    }
}
