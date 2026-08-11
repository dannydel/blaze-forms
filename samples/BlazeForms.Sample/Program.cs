using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Sample.Components;
using BlazeForms.Sample.Data;
using BlazeForms.Sample.Services;
using BlazeForms.Versioning;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddLocalization();

// BlazeForms ships no DI extension of its own (the agnosticism invariant, AGENTS.md #1) — a host
// registers whichever IFormDefinitionStore/IFormDraftStore/IFormSubmissionSink implementations it
// wants by hand. This sample registers the in-memory ones the library ships for demos, all as
// singletons so the seeded reference form and every submission survive across circuits and
// requests for the process's lifetime — an ordinary scoped/transient lifetime would lose both on
// the very next request.
var definitionStore = new InMemoryFormDefinitionStore();
await SeedEnrollmentFormAsync(definitionStore).ConfigureAwait(false);
builder.Services.AddSingleton<IFormDefinitionStore>(definitionStore);
builder.Services.AddSingleton<IFormDraftStore, InMemoryFormDraftStore>();
builder.Services.AddSingleton<SampleSubmissionSink>();
builder.Services.AddSingleton<IFormSubmissionSink>(sp => sp.GetRequiredService<SampleSubmissionSink>());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync().ConfigureAwait(false);

// Seeds and publishes the reference enrollment form (PRD §14 success criterion #2: 3 pages, 2
// conditional branches) so every page the sample serves has a form to load — the in-memory store
// otherwise starts out empty on every process start. Runs before the host is built rather than as
// a hosted service, since nothing needs to await startup work beyond this and the in-memory
// store's operations complete synchronously anyway.
static async Task SeedEnrollmentFormAsync(IFormDefinitionStore definitionStore)
{
    var draft = FormLifecycle.CreateDraft(EnrollmentForm.Build());
    await definitionStore.SaveDraftAsync(draft).ConfigureAwait(false);
    await definitionStore.PublishAsync(draft.FormId, "Initial reference enrollment form.", "sample-host").ConfigureAwait(false);
}
