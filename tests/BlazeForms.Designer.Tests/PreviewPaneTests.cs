using BlazeForms.Definitions;
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Preview;
using BlazeForms.Versioning;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="PreviewPane"/> in isolation (PRD §4.1): it renders the working draft through
/// the real <see cref="BlazeForms.FormRenderer"/> with <see cref="BlazeForms.FormRenderer.Ephemeral"/>
/// set, so filling and submitting inside it never reaches a registered <see cref="IFormSubmissionSink"/>
/// or <see cref="IFormDraftStore"/>, live conditional visibility still works, and its own Exit
/// button raises <see cref="PreviewPane.OnExit"/>.
/// </summary>
public sealed class PreviewPaneTests : DesignerTestContext
{
    private sealed class RecordingSink : IFormSubmissionSink
    {
        public int SubmitCount { get; private set; }

        public Task SubmitAsync(FormSubmissionEnvelope envelope, CancellationToken cancellationToken = default)
        {
            SubmitCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDraftStore : IFormDraftStore
    {
        public int LoadCount { get; private set; }

        public int SaveCount { get; private set; }

        public int DeleteCount { get; private set; }

        public Task<FormDraft?> LoadAsync(FormDraftKey key, CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult<FormDraft?>(null);
        }

        public Task SaveAsync(FormDraft draft, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(FormDraftKey key, CancellationToken cancellationToken = default)
        {
            DeleteCount++;
            return Task.CompletedTask;
        }
    }

    private static DesignerEditContext CreateContext(FormDefinition definition, IFormDefinitionStore? store = null) =>
        new(FormLifecycle.CreateDraft(definition), store ?? new InMemoryFormDefinitionStore());

    [Fact]
    public async Task RendersTheDraftThroughFormRendererAndFillingItAndSubmittingNeverReachesARegisteredStoreOrSink()
    {
        var sink = new RecordingSink();
        var draftStore = new RecordingDraftStore();
        Services.AddSingleton<IFormSubmissionSink>(sink);
        Services.AddSingleton<IFormDraftStore>(draftStore);

        await using var editContext = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        var cut = Render<PreviewPane>(p => p.Add(f => f.EditContext, editContext));

        // The hosted FormRenderer is really rendering "first-name" from the draft, not a stub.
        var field = cut.Find("input[id$='-first-name']");
        await field.InputAsync(new ChangeEventArgs { Value = "Ada" });
        await field.BlurAsync(new FocusEventArgs());
        await cut.FindAll("div.bf-step-nav button")[1].ClickAsync(new MouseEventArgs()); // Submit -- nothing here is required.

        Assert.Equal(0, draftStore.LoadCount);
        Assert.Equal(0, draftStore.SaveCount);
        Assert.Equal(0, draftStore.DeleteCount);
        Assert.Equal(0, sink.SubmitCount);
        Assert.NotEmpty(cut.FindAll(".bf-confirmation"));
    }

    [Fact]
    public async Task LiveConditionalVisibilityWorksInsidePreview()
    {
        await using var editContext = CreateContext(DesignerTestFixtures.ReferencedFieldDefinition("form-1"));
        var cut = Render<PreviewPane>(p => p.Add(f => f.EditContext, editContext));

        // "node-dependent" is only visible once "node-referenced" is non-blank -- proves the real
        // VisibilityEvaluator is live inside the preview, not a static snapshot of the draft.
        Assert.Empty(cut.FindAll("input[id$='-node-dependent']"));

        await cut.Find("input[id$='-node-referenced']").InputAsync(new ChangeEventArgs { Value = "a value" });

        Assert.NotEmpty(cut.FindAll("input[id$='-node-dependent']"));
    }

    [Fact]
    public async Task TheExitButtonRaisesOnExit()
    {
        await using var editContext = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        var exited = false;
        var cut = Render<PreviewPane>(p => p
            .Add(f => f.EditContext, editContext)
            .Add(f => f.OnExit, () => exited = true));

        await cut.Find("button.bf-preview-pane__exit-button").ClickAsync(new MouseEventArgs());

        Assert.True(exited);
    }

    [Fact]
    public async Task MovesFocusToItsOwnHeadingOnFirstRender()
    {
        await using var editContext = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));

        Render<PreviewPane>(p => p.Add(f => f.EditContext, editContext));

        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public async Task NeverMutatesTheDraftItPreviews()
    {
        await using var editContext = CreateContext(DesignerTestFixtures.OneFieldDefinition("form-1"));
        var originalDefinition = editContext.Draft.Definition;

        var cut = Render<PreviewPane>(p => p.Add(f => f.EditContext, editContext));
        await cut.Find("input[id$='-first-name']").InputAsync(new ChangeEventArgs { Value = "Ada" });

        Assert.Same(originalDefinition, editContext.Draft.Definition);
        Assert.False(editContext.IsDirty);
    }
}
