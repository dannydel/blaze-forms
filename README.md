# BlazeForms

[![NuGet BlazeForms.Core](https://img.shields.io/nuget/vpre/BlazeForms.Core?label=BlazeForms.Core)](https://www.nuget.org/packages/BlazeForms.Core)
[![NuGet BlazeForms.Renderer](https://img.shields.io/nuget/vpre/BlazeForms.Renderer?label=BlazeForms.Renderer)](https://www.nuget.org/packages/BlazeForms.Renderer)
[![NuGet BlazeForms.Designer](https://img.shields.io/nuget/vpre/BlazeForms.Designer?label=BlazeForms.Designer)](https://www.nuget.org/packages/BlazeForms.Designer)

**Versioned, accessible, UI-library-agnostic forms for Blazor.** Define forms as data, build them in a keyboard-first designer, render a published version in any Blazor app, and review submissions against the exact version that captured them.

> ⚠️ **Pre-release.** The API is unstable through 0.x. BlazeForms targets .NET 10 and requires an interactive Blazor render mode for filling and designing forms. See [docs/PRD.md](https://github.com/dannydel/blaze-forms/blob/main/docs/PRD.md) for the locked product scope and phasing.

## Packages

| Package | Purpose | Depends on |
|---|---|---|
| `BlazeForms.Core` | Definition schema, serialization, expressions, linting, versioning, and host contracts. No UI. | BCL + Markdig |
| `BlazeForms.Renderer` | `<FormRenderer>`, `<FormSubmissionView>`, default field components, and neutral CSS tokens. | Core |
| `BlazeForms.Designer` | `<FormDesigner>` and `<FormLibrary>`. | Core + Renderer |

## Quick start

Install the package that provides the surface you need. `BlazeForms.Renderer` brings in Core; `BlazeForms.Designer` brings in both Core and Renderer.

```bash
dotnet add package BlazeForms.Renderer --prerelease
# Optional: add the authoring experience.
dotnet add package BlazeForms.Designer --prerelease
```

When developing from this repository, use the corresponding project references instead.

### 1. Configure an interactive Blazor host

BlazeForms does not provide storage, authentication, or an HTTP API. Register implementations of the contracts your host uses. The in-memory implementations are appropriate for a demo or test host only.

```csharp
using BlazeForms.Hosting;
using BlazeForms.Hosting.InMemory;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddLocalization();

var definitionStore = new InMemoryFormDefinitionStore();
builder.Services.AddSingleton<IFormDefinitionStore>(definitionStore);
builder.Services.AddSingleton<IFormDraftStore, InMemoryFormDraftStore>();

var app = builder.Build();

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
```

Use `AddInteractiveWebAssemblyComponents` or another interactive mode when that better fits the host. Static SSR can render neither a fill nor the designer.

### 2. Add the stylesheets

Add the renderer theme to the host page's `<head>`. Keep the app's generated `*.styles.css` stylesheet in place; Blazor uses it to load isolated component CSS.

```razor
<link rel="stylesheet" href="_content/BlazeForms.Renderer/blazeforms.css" />
```

For the designer, add its stylesheet too:

```razor
<link rel="stylesheet" href="_content/BlazeForms.Renderer/blazeforms.css" />
<link rel="stylesheet" href="_content/BlazeForms.Designer/blazeforms-designer.css" />
```

### 3. Create and publish a definition

Definitions are immutable values. Use generated IDs; labels are display content and never keys. In production, authors normally create this content with `<FormDesigner>` and the host persists it through `IFormDefinitionStore`.

```csharp
using BlazeForms.Definitions;
using BlazeForms.Hosting;
using BlazeForms.Versioning;

static async Task<FormVersion> CreateEnrollmentFormAsync(IFormDefinitionStore store)
{
    var definition = new FormDefinition
    {
        Id = FormIds.NewFormId(),
        Name = "Enrollment",
        Pages =
        [
            new FormPage
            {
                Id = FormIds.NewPageId(),
                Title = "About you",
                Sections =
                [
                    new FormSection
                    {
                        Id = FormIds.NewSectionId(),
                        Title = "Contact details",
                        Nodes =
                        [
                            new FormNode
                            {
                                Id = FormIds.NewNodeId(),
                                Type = NodeType.Text,
                                Label = "Full legal name",
                                Required = true,
                            },
                            new FormNode
                            {
                                Id = FormIds.NewNodeId(),
                                Type = NodeType.Email,
                                Label = "Email address",
                                Required = true,
                            },
                        ],
                    },
                ],
            },
        ],
    };

    var draft = FormLifecycle.CreateDraft(definition);
    await store.SaveDraftAsync(draft);

    return await store.PublishAsync(
        definition.Id,
        "Initial enrollment form.",
        "system");
}
```

Publishing creates v1 and consumes the draft. To change a published form, start a new draft and publish a new version; never alter an existing published version.

### 4. Render a published form

Load one published `FormVersion` when a respondent starts. Keep that version for the fill; a later publication is a new fill, not a live update to an in-progress response.

```razor
@using BlazeForms
@using BlazeForms.Hosting
@using BlazeForms.Versioning
@inject IFormDefinitionStore FormDefinitions

@if (_version is not null)
{
    <FormRenderer Version="@_version"
                  RespondentKey="@RespondentKey"
                  OnSubmitted="HandleSubmittedAsync" />
}

@code {
    private const string FormId = "your-stable-form-id";
    private const string RespondentKey = "current-user-id";
    private FormVersion? _version;

    protected override async Task OnInitializedAsync() =>
        _version = await FormDefinitions.GetLatestPublishedVersionAsync(FormId);

    private Task HandleSubmittedAsync(FormSubmissionEnvelope envelope)
    {
        // Persist, enqueue, or navigate. Values are keyed by immutable node ID.
        return Task.CompletedTask;
    }
}
```

`RespondentKey` activates `IFormDraftStore` autosave and resume behavior. Omit it for anonymous fills. You can handle submissions with `OnSubmitted`, register `IFormSubmissionSink`, or use both.

## Design forms

`FormDesigner` resolves `IFormDefinitionStore` from DI and edits its working draft. `FormLibrary` lists forms from the same store and leaves navigation to the host.

```razor
@using BlazeForms
@using BlazeForms.Versioning
@inject NavigationManager Navigation

<FormDesigner FormId="@FormId"
              Author="@CurrentUserName"
              OnPublished="HandlePublishedAsync" />

<FormLibrary OnOpenInDesigner="OpenDesignerAsync" />

@code {
    private const string FormId = "your-stable-form-id";
    private const string CurrentUserName = "current-user";

    private Task HandlePublishedAsync(FormVersion version) => Task.CompletedTask;

    private Task OpenDesignerAsync(string formId)
    {
        Navigation.NavigateTo($"/forms/{formId}/design");
        return Task.CompletedTask;
    }
}
```

The designer has keyboard parity for structural actions, maintains undo/redo history, announces mutations, and blocks publishing on its built-in blocking lints. It keeps Markdown-enabled author content (`help`, `paragraph`, and `callout`) on Core's safe Markdown pipeline; labels, options, validation messages, and all respondent answers remain plain text.

## Review submissions

Persist each `FormSubmissionEnvelope` with its `FormId` and `DefinitionVersion`. When reviewing it, load that exact definition version rather than the latest published form.

```razor
<FormSubmissionView Envelope="@submission"
                    Version="@capturedVersion"
                    LatestPublishedVersion="@latestPublishedVersion" />
```

`FormSubmissionView` renders hidden fields as not applicable and offers a JSON export of the envelope.

## Host contracts

| Contract | Host responsibility |
|---|---|
| `IFormDefinitionStore` | Store draft, published, and retired versions. Preserve published versions forever and number versions monotonically. |
| `IFormDraftStore` | Persist in-progress answers by form, definition version, and respondent key. Define retention and expiry policy. |
| `IFormSubmissionSink` | Take ownership of a completed submission: persist it, start workflow, notify, or enqueue work. |
| `IFieldComponentRegistry` | Optionally replace default fields with host design-system components per `NodeType`. |

The library intentionally ships no database, authentication scheme, tenant model, or workflow engine. Production stores and submission handlers should enforce authorization, use idempotent submission persistence, and retain the captured definition version alongside each submission.

## JSON Schema

The definition format has a published [JSON Schema](https://github.com/dannydel/blaze-forms/blob/main/docs/schemas/form-definition-v3.schema.json) for editor tooling and validation. See [docs/schema.md](https://github.com/dannydel/blaze-forms/blob/main/docs/schema.md) for how to consume it and the versioning policy.

## Theming and component replacement

The default renderer is neutral HTML and CSS. Restyle it through the documented `--bf-*` token contract, scoped globally or around one form. See [docs/theming.md](https://github.com/dannydel/blaze-forms/blob/main/docs/theming.md) for every token and a Bootstrap mapping.

For a host UI library, implement `IFieldComponentRegistry` and map individual `NodeType` values to compatible Blazor components. The `samples/BlazeForms.Sample/Mud` adapter demonstrates the seam with MudBlazor without adding a vendor dependency to any BlazeForms package.

## What ships now

- Schema, serialization, version lifecycle, safe Markdown, visibility expressions, and cross-field validation rules.
- Renderer, submission view, default semantic field components, drafts, validation, and JSON export.
- Keyboard-first designer, library, linter, version history, preview, and a MudBlazor sample adapter.
- P1 fields: text, textarea, email, phone, number, currency, date, date range, select, radio, checkbox group, yes/no, boolean, heading, paragraph, callout, divider, and a read-only calc placeholder.

Repeating groups, file upload, lookup fields, calculated-value evaluation, and localized form content are planned follow-on work. See [docs/PRD.md](https://github.com/dannydel/blaze-forms/blob/main/docs/PRD.md) for the full phased roadmap.

## Development

```bash
dotnet test BlazeForms.sln
dotnet test tests/BlazeForms.E2E.Tests/BlazeForms.E2E.Tests.csproj
```

The E2E project runs the sample host with Playwright and axe. If Chromium is not already installed, build the project and run its generated Playwright installer:

```bash
dotnet build tests/BlazeForms.E2E.Tests/BlazeForms.E2E.Tests.csproj
pwsh tests/BlazeForms.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

See [CONTRIBUTING.md](https://github.com/dannydel/blaze-forms/blob/main/CONTRIBUTING.md) and [AGENTS.md](https://github.com/dannydel/blaze-forms/blob/main/AGENTS.md) for contribution, test, accessibility, and DCO requirements.

## License

[MIT](https://github.com/dannydel/blaze-forms/blob/main/LICENSE) © Daniel Del Grosso and contributors
