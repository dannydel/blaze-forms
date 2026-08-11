using BlazeForms.Definitions;
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Linting;
using BlazeForms.Versioning;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="PublishDialog"/>: the publish gate re-lints on open and again on confirm, so
/// a blocking issue -- present from the start, or introduced after this dialog opened -- disables
/// Confirm and lists every blocker with a jump-to-node action; once clean, Confirm stays disabled
/// until the change note is non-empty, then calls <see cref="IFormDefinitionStore.PublishAsync"/>
/// exactly once with the right arguments; a null <see cref="PublishDialog.Author"/> publishes with
/// the localized "Unknown"; and it satisfies the standard trapped-dialog contract (PRD §7, §11).
/// </summary>
public sealed class PublishDialogTests : DesignerTestContext
{
    private static DesignerEditContext CreateContext(FormDefinition definition, IFormDefinitionStore? store = null) =>
        new(FormLifecycle.CreateDraft(definition), store ?? new InMemoryFormDefinitionStore());

    [Fact]
    public async Task RendersAsAFocusLabelledModalDialog()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        var cut = Render<PublishDialog>(p => p.Add(d => d.EditContext, context).Add(d => d.Store, new InMemoryFormDefinitionStore()));

        var dialog = cut.Find("div.bf-publish-dialog");
        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        var labelledBy = dialog.GetAttribute("aria-labelledby");
        Assert.NotNull(labelledBy);
        Assert.Equal(cut.Find("h2").Id, labelledBy);
    }

    [Fact]
    public async Task ABlockingIssuePresentOnOpenDisablesConfirmAndListsItWithAJumpButton()
    {
        await using var context = CreateContext(DesignerTestFixtures.UntitledNodeDefinition("form-1"));
        var cut = Render<PublishDialog>(p => p.Add(d => d.EditContext, context).Add(d => d.Store, new InMemoryFormDefinitionStore()));

        Assert.Contains(cut.Instance.LintResults, r => r.RuleId == LintRuleIds.A11y01);
        Assert.True(cut.Find("button.bf-publish-dialog__button--primary").HasAttribute("disabled"));
        Assert.NotEmpty(cut.FindAll("li.bf-publish-dialog__blocker"));
        var jumpButton = cut.Find("button.bf-publish-dialog__jump");

        await jumpButton.ClickAsync(new MouseEventArgs());

        Assert.Equal("node-untitled", context.Selection.NodeId);
        Assert.Equal(DesignerFocusIntent.JumpedTo, context.Selection.Intent);
    }

    [Fact]
    public async Task AnEmptyChangeNoteKeepsConfirmDisabledAndANonEmptyOneEnablesIt()
    {
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        var cut = Render<PublishDialog>(p => p.Add(d => d.EditContext, context).Add(d => d.Store, new InMemoryFormDefinitionStore()));

        Assert.Empty(cut.FindAll("li.bf-publish-dialog__blocker"));
        Assert.True(cut.Find("button.bf-publish-dialog__button--primary").HasAttribute("disabled"));

        await cut.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Renamed the employer field." });

        Assert.False(cut.Find("button.bf-publish-dialog__button--primary").HasAttribute("disabled"));
    }

    [Fact]
    public async Task ConfirmPublishesExactlyOnceWithTheFormIdNoteAndAuthorAndRaisesOnPublished()
    {
        var store = new RecordingFormDefinitionStore();
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"), store);
        FormVersion? raisedVersion = null;
        var closed = false;
        var cut = Render<PublishDialog>(p => p
            .Add(d => d.EditContext, context)
            .Add(d => d.Store, store)
            .Add(d => d.Author, "jane.doe")
            .Add(d => d.OnPublished, (FormVersion v) => raisedVersion = v)
            .Add(d => d.OnClosed, () => closed = true));

        await cut.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Initial publish." });
        await cut.Find("button.bf-publish-dialog__button--primary").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, store.PublishCallCount);
        Assert.Equal(("form-1", "Initial publish.", "jane.doe"), store.LastPublishArgs);
        Assert.NotNull(raisedVersion);
        Assert.Equal(1, raisedVersion!.Version);
        Assert.True(closed);
    }

    [Fact]
    public async Task NullAuthorPublishesWithTheLocalizedUnknownFallback()
    {
        var store = new RecordingFormDefinitionStore();
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"), store);
        var cut = Render<PublishDialog>(p => p
            .Add(d => d.EditContext, context)
            .Add(d => d.Store, store)
            .Add(d => d.Author, (string?)null));

        await cut.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Initial publish." });
        await cut.Find("button.bf-publish-dialog__button--primary").ClickAsync(new MouseEventArgs());

        Assert.Equal("Unknown", store.LastPublishArgs!.Value.Author);
    }

    [Fact]
    public async Task TocTouABlockingIssueIntroducedAfterOpenBlocksConfirmOnReLint()
    {
        var store = new RecordingFormDefinitionStore();
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"), store);
        var cut = Render<PublishDialog>(p => p
            .Add(d => d.EditContext, context)
            .Add(d => d.Store, store));

        // Dialog opened onto a clean draft, and the author has already typed a note -- Confirm
        // would have gone straight through by this dialog's own opening picture.
        await cut.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Initial publish." });
        Assert.False(cut.Find("button.bf-publish-dialog__button--primary").HasAttribute("disabled"));

        // Something else mutates the SAME draft this dialog is holding open, introducing a
        // blocking A11Y-01 finding this dialog's own OnInitialized lint pass never saw.
        var node = context.Draft.Definition.FindNode("first-name")!;
        context.UpdateNode(node with { Label = null });
        cut.Render();

        await cut.Find("button.bf-publish-dialog__button--primary").ClickAsync(new MouseEventArgs());

        Assert.Equal(0, store.PublishCallCount);
        Assert.NotEmpty(cut.FindAll("li.bf-publish-dialog__blocker"));
        Assert.True(cut.Find("button.bf-publish-dialog__button--primary").HasAttribute("disabled"));
    }

    [Fact]
    public async Task EscCancelsWithoutPublishingAndRaisesOnClosed()
    {
        var store = new RecordingFormDefinitionStore();
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"), store);
        var closed = false;
        var cut = Render<PublishDialog>(p => p
            .Add(d => d.EditContext, context)
            .Add(d => d.Store, store)
            .Add(d => d.OnClosed, () => closed = true));

        await cut.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Would-be note." });
        await cut.Find("div.bf-publish-dialog").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(0, store.PublishCallCount);
        Assert.True(closed);
    }

    [Fact]
    public async Task CancelButtonCancelsWithoutPublishing()
    {
        var store = new RecordingFormDefinitionStore();
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"), store);
        var closed = false;
        var cut = Render<PublishDialog>(p => p
            .Add(d => d.EditContext, context)
            .Add(d => d.Store, store)
            .Add(d => d.OnClosed, () => closed = true));

        await cut.Find("button.bf-publish-dialog__button:not(.bf-publish-dialog__button--primary)").ClickAsync(new MouseEventArgs());

        Assert.Equal(0, store.PublishCallCount);
        Assert.True(closed);
    }

    [Fact]
    public async Task TheFocusTrapModuleIsImportedFocusesCancelAndIsDisposed()
    {
        var module = JSInterop.SetupModule(PublishDialog.ModulePath);
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        var cut = Render<PublishDialog>(p => p.Add(d => d.EditContext, context).Add(d => d.Store, new InMemoryFormDefinitionStore()));

        cut.WaitForAssertion(() => Assert.True(cut.Instance.HasImportedModule));
        module.VerifyInvoke("attachFocusTrap");
        JSInterop.VerifyFocusAsyncInvoke();

        await cut.Instance.DisposeAsync();

        Assert.False(cut.Instance.HasImportedModule);
    }

    [Fact]
    public async Task ReenteringConfirmAsyncWhileAPriorCallIsStillInFlightPublishesExactlyOnce()
    {
        // Models a fast double-click under an async host store: the browser delivers a second
        // onclick dispatch before the first call's own _publishing-setting statement has had a
        // chance to make its way back to the DOM as a re-render that disables the Confirm button
        // (PublishDialog.ConfirmAsync's own remarks). Calling ConfirmAsync directly, twice, without
        // awaiting the first in between, reproduces exactly that race deterministically -- the same
        // approach FormRendererSubmissionTests uses for FormRenderer.SubmitAsync's own guard.
        var store = new RecordingFormDefinitionStore();
        await using var context = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"), store);
        var cut = Render<PublishDialog>(p => p
            .Add(d => d.EditContext, context)
            .Add(d => d.Store, store)
            .Add(d => d.Author, "jane.doe"));

        await cut.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Initial publish." });

        var firstCall = cut.Instance.ConfirmAsync();
        var secondCall = cut.Instance.ConfirmAsync();
        await Task.WhenAll(firstCall, secondCall);

        Assert.Equal(1, store.PublishCallCount);
        Assert.Equal(1, store.SaveDraftCallCount);
    }
}

/// <summary>
/// An <see cref="IFormDefinitionStore"/> decorator wrapping <see cref="InMemoryFormDefinitionStore"/>
/// that records every <see cref="SaveDraftAsync"/>, <see cref="PublishAsync"/>, and
/// <see cref="RetireAsync"/> call, so a test can assert exactly how many times, and with which
/// arguments, this dialog actually called the store -- a plain
/// <see cref="InMemoryFormDefinitionStore"/> has no way to observe that on its own.
/// </summary>
internal sealed class RecordingFormDefinitionStore : IFormDefinitionStore
{
    private readonly InMemoryFormDefinitionStore _inner = new();

    public int SaveDraftCallCount { get; private set; }

    public int PublishCallCount { get; private set; }

    public int RetireCallCount { get; private set; }

    public (string FormId, string ChangeNote, string Author)? LastPublishArgs { get; private set; }

    public (string FormId, int Version)? LastRetireArgs { get; private set; }

    public Task<FormVersion?> GetVersionAsync(string formId, int version, CancellationToken cancellationToken = default) =>
        _inner.GetVersionAsync(formId, version, cancellationToken);

    public Task<FormVersion?> GetLatestPublishedVersionAsync(string formId, CancellationToken cancellationToken = default) =>
        _inner.GetLatestPublishedVersionAsync(formId, cancellationToken);

    public Task<FormVersion?> GetDraftAsync(string formId, CancellationToken cancellationToken = default) =>
        _inner.GetDraftAsync(formId, cancellationToken);

    public Task SaveDraftAsync(FormVersion draft, CancellationToken cancellationToken = default)
    {
        SaveDraftCallCount++;
        return _inner.SaveDraftAsync(draft, cancellationToken);
    }

    public Task DeleteDraftAsync(string formId, CancellationToken cancellationToken = default) =>
        _inner.DeleteDraftAsync(formId, cancellationToken);

    public async Task<FormVersion> PublishAsync(
        string formId,
        string changeNote,
        string author,
        CancellationToken cancellationToken = default)
    {
        PublishCallCount++;
        LastPublishArgs = (formId, changeNote, author);
        return await _inner.PublishAsync(formId, changeNote, author, cancellationToken).ConfigureAwait(false);
    }

    public async Task RetireAsync(string formId, int version, CancellationToken cancellationToken = default)
    {
        RetireCallCount++;
        LastRetireArgs = (formId, version);
        await _inner.RetireAsync(formId, version, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<FormVersionSummary>> ListVersionsAsync(
        string formId,
        CancellationToken cancellationToken = default) =>
        _inner.ListVersionsAsync(formId, cancellationToken);

    public Task<IReadOnlyList<FormVersionSummary>> ListFormsAsync(CancellationToken cancellationToken = default) =>
        _inner.ListFormsAsync(cancellationToken);
}
