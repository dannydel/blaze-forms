using BlazeForms.Demo.Wasm;
using BlazeForms.Demo.Wasm.Services;
using BlazeForms.Sample.Data;
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Versioning;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// BlazeForms ships no DI extension of its own (the agnosticism invariant, AGENTS.md #1) — this
// demo registers the same in-memory implementations the sample host does, all as singletons.
// A WASM singleton lives for the tab, not the process, which is exactly right here: the demo's
// banner already tells the visitor a refresh loses everything.
var definitionStore = new InMemoryFormDefinitionStore();
await SeedEnrollmentFormAsync(definitionStore).ConfigureAwait(false);
builder.Services.AddSingleton<IFormDefinitionStore>(definitionStore);
builder.Services.AddSingleton<IFormDraftStore, InMemoryFormDraftStore>();
builder.Services.AddSingleton<DemoSubmissionSink>();
builder.Services.AddSingleton<IFormSubmissionSink>(sp => sp.GetRequiredService<DemoSubmissionSink>());

await builder.Build().RunAsync().ConfigureAwait(false);

// Seeds and publishes the reference enrollment form (shared with the sample host via a linked
// compile item) so every demo page has a form to load — the in-memory store otherwise starts out
// empty on every fresh tab.
static async Task SeedEnrollmentFormAsync(IFormDefinitionStore definitionStore)
{
    var definition = EnrollmentForm.Build();
    var draft = FormLifecycle.CreateDraft(definition);
    await definitionStore.SaveDraftAsync(draft).ConfigureAwait(false);

    // Published with the seed's own Owner (definition.Owner is "sample-host", baked into
    // EnrollmentForm.Build() itself) rather than a "demo-host" literal, so the version-history
    // author and the definition's Owner agree wherever either one is displayed. Owner is
    // nullable on FormDefinition in general; this particular seed always sets it.
    await definitionStore.PublishAsync(draft.FormId, "Initial reference enrollment form.", definition.Owner ?? "demo-host").ConfigureAwait(false);
}
