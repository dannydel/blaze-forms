using BlazeForms.Serialization;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Covers <see cref="FormSubmissionView"/>'s JSON export (PRD §4.3): the collocated JS module is
/// imported lazily on the respondent's first click, invoked with the exact indented envelope JSON
/// <see cref="FormJson.SerializeEnvelope"/> produces, and disposed by <c>DisposeAsync</c>.
/// </summary>
public sealed class FormSubmissionViewExportTests : RendererTestContext
{
    // Loose mode: the module import itself is awaited inside an event handler dispatched through
    // bUnit's renderer, same rationale SortableBlazor's own JS-interop tests give for this choice.
    public FormSubmissionViewExportTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private IRenderedComponent<FormSubmissionView> RenderView()
    {
        var version = FormSubmissionViewTestFixtures.ToVersion(FormSubmissionViewTestFixtures.SubmissionViewDefinition);
        var envelope = FormSubmissionViewTestFixtures.BuildEnvelope(
            FormSubmissionViewTestFixtures.SubmissionViewDefinition,
            version.Version,
            FormSubmissionViewTestFixtures.SubmissionViewValues);

        return Render<FormSubmissionView>(p => p
            .Add(f => f.Envelope, envelope)
            .Add(f => f.Version, version));
    }

    [Fact]
    public void RenderingAloneNeverImportsTheModule()
    {
        JSInterop.SetupModule(FormSubmissionView.ModulePath);

        var cut = RenderView();

        // The import must wait for a genuine click -- see FormSubmissionView.razor.cs's remarks
        // on why that is also what keeps it prerender-safe.
        Assert.False(cut.Instance.HasImportedModule);
    }

    [Fact]
    public void ClickingExportImportsTheModuleAndInvokesItWithTheIndentedEnvelopeJson()
    {
        var module = JSInterop.SetupModule(FormSubmissionView.ModulePath);
        var cut = RenderView();

        cut.Find(".bf-submission__export").Click();

        var invocation = module.VerifyInvoke("downloadSubmissionJson");
        Assert.Equal(2, invocation.Arguments.Count);
        Assert.Equal(
            FormJson.SerializeEnvelope(cut.Instance.Envelope, indented: true),
            invocation.Arguments[1]);
        Assert.True(cut.Instance.HasImportedModule);
    }

    [Fact]
    public async Task DisposeAsyncDisposesTheImportedModuleReference()
    {
        JSInterop.SetupModule(FormSubmissionView.ModulePath);
        var cut = RenderView();
        await cut.Find(".bf-submission__export").ClickAsync(new MouseEventArgs());
        Assert.True(cut.Instance.HasImportedModule);

        await cut.Instance.DisposeAsync();

        Assert.False(cut.Instance.HasImportedModule);
    }

    [Fact]
    public async Task DisposeAsyncIsANoOpWhenExportWasNeverClicked()
    {
        JSInterop.SetupModule(FormSubmissionView.ModulePath);
        var cut = RenderView();

        await cut.Instance.DisposeAsync();

        Assert.False(cut.Instance.HasImportedModule);
    }
}
