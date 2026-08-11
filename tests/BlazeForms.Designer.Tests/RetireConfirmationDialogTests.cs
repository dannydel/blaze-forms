using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Versioning;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="RetireConfirmationDialog"/> in isolation: it confirms through
/// <see cref="IFormDefinitionStore.RetireAsync"/> and closes, cancels (<c>Esc</c> and the Cancel
/// button) without touching the store, and satisfies its own <c>role="dialog" aria-modal="true"</c>/
/// focus-trap contract with the safe Cancel action focused by default (PRD §7, §11).
/// </summary>
public sealed class RetireConfirmationDialogTests : DesignerTestContext
{
    private static async Task<RecordingFormDefinitionStore> SeedPublishedStoreAsync(string formId)
    {
        var store = new RecordingFormDefinitionStore();
        await store.SaveDraftAsync(FormLifecycle.CreateDraft(DesignerTestFixtures.OneFieldDefinition(formId))).ConfigureAwait(true);
        await store.PublishAsync(formId, "Initial publish.", "author-1").ConfigureAwait(true);
        return store;
    }

    [Fact]
    public async Task RendersAsAFocusLabelledModalDialogNamingTheVersion()
    {
        var store = await SeedPublishedStoreAsync("form-1");
        var cut = Render<RetireConfirmationDialog>(p => p.Add(d => d.FormId, "form-1").Add(d => d.Store, store).Add(d => d.Version, 1));

        var dialog = cut.Find("div.bf-retire-dialog");
        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.Equal(cut.Find("h2").Id, dialog.GetAttribute("aria-labelledby"));
        Assert.Contains('1', cut.Find("h2").TextContent);
    }

    [Fact]
    public async Task ConfirmRetiresTheExactVersionAndRaisesOnClosed()
    {
        var store = await SeedPublishedStoreAsync("form-1");
        var closed = false;
        var cut = Render<RetireConfirmationDialog>(p => p
            .Add(d => d.FormId, "form-1")
            .Add(d => d.Store, store)
            .Add(d => d.Version, 1)
            .Add(d => d.OnClosed, () => closed = true));

        await cut.Find("button.bf-retire-dialog__button--danger").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, store.RetireCallCount);
        Assert.Equal(("form-1", 1), store.LastRetireArgs);
        Assert.True(closed);
        var summary = (await store.ListVersionsAsync("form-1")).Single();
        Assert.Equal(FormLifecycleState.Retired, summary.State);
    }

    [Fact]
    public async Task EscCancelsWithoutRetiringAndRaisesOnClosed()
    {
        var store = await SeedPublishedStoreAsync("form-1");
        var closed = false;
        var cut = Render<RetireConfirmationDialog>(p => p
            .Add(d => d.FormId, "form-1")
            .Add(d => d.Store, store)
            .Add(d => d.Version, 1)
            .Add(d => d.OnClosed, () => closed = true));

        await cut.Find("div.bf-retire-dialog").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(0, store.RetireCallCount);
        Assert.True(closed);
    }

    [Fact]
    public async Task CancelButtonCancelsWithoutRetiring()
    {
        var store = await SeedPublishedStoreAsync("form-1");
        var cut = Render<RetireConfirmationDialog>(p => p.Add(d => d.FormId, "form-1").Add(d => d.Store, store).Add(d => d.Version, 1));

        await cut.Find("button.bf-retire-dialog__button:not(.bf-retire-dialog__button--danger)").ClickAsync(new MouseEventArgs());

        Assert.Equal(0, store.RetireCallCount);
    }

    [Fact]
    public async Task TheFocusTrapModuleIsImportedFocusesCancelAndIsDisposed()
    {
        var module = JSInterop.SetupModule(RetireConfirmationDialog.ModulePath);
        var store = await SeedPublishedStoreAsync("form-1");
        var cut = Render<RetireConfirmationDialog>(p => p.Add(d => d.FormId, "form-1").Add(d => d.Store, store).Add(d => d.Version, 1));

        cut.WaitForAssertion(() => Assert.True(cut.Instance.HasImportedModule));
        module.VerifyInvoke("attachFocusTrap");
        JSInterop.VerifyFocusAsyncInvoke();

        await cut.Instance.DisposeAsync();

        Assert.False(cut.Instance.HasImportedModule);
    }

    [Fact]
    public async Task ReenteringConfirmAsyncWhileAPriorCallIsStillInFlightRetiresExactlyOnce()
    {
        // Models a fast double-click under an async host store: the browser delivers a second
        // onclick dispatch before the first call's own _retiring-setting statement has had a
        // chance to make its way back to the DOM as a re-render that disables the "Retire" button
        // (RetireConfirmationDialog.ConfirmAsync's own remarks). Calling ConfirmAsync directly,
        // twice, without awaiting the first in between, reproduces exactly that race
        // deterministically -- the same approach FormRendererSubmissionTests uses for
        // FormRenderer.SubmitAsync's own guard.
        var store = await SeedPublishedStoreAsync("form-1");
        var cut = Render<RetireConfirmationDialog>(p => p.Add(d => d.FormId, "form-1").Add(d => d.Store, store).Add(d => d.Version, 1));

        var firstCall = cut.Instance.ConfirmAsync();
        var secondCall = cut.Instance.ConfirmAsync();
        await Task.WhenAll(firstCall, secondCall);

        Assert.Equal(1, store.RetireCallCount);
    }
}
