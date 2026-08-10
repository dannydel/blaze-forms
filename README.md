# BlazeForms

**Versioned, accessible, UI-library-agnostic forms for Blazor.** Define forms as data in a keyboard-first designer, render them in any Blazor app, and review submissions against the exact version they were captured with.

> ⚠️ Pre-release. The API is unstable through 0.x. See [docs/PRD.md](docs/PRD.md) for scope and phasing.

## Packages

| Package | What it gives you |
|---|---|
| `BlazeForms.Core` | Schema, expression engine, validation, linter, versioning, host contracts — no UI |
| `BlazeForms.Renderer` | `<FormRenderer>` and `<FormSubmissionView>` |
| `BlazeForms.Designer` | `<FormDesigner>` and `<FormLibrary>` |

## Why BlazeForms

- **Bring your own UI library** — theme everything through the `--bf-*` CSS token contract, or swap field components wholesale via `IFieldComponentRegistry`. No dependency on any component vendor.
- **Accessibility as a feature** — WCAG 2.2 AA, full keyboard parity in the designer, and a publish-gating linter that catches unlabelled inputs and dangling logic before respondents ever see them.
- **Published means immutable** — submissions render forever against the definition version they were captured with.
- **You own the data** — persistence is four small interfaces (`IFormDefinitionStore`, `IFormSubmissionSink`, `IFormDraftStore`, `IFieldComponentRegistry`); the library ships no database, HTTP, or auth.

## Contributing

Start with [CONTRIBUTING.md](CONTRIBUTING.md); the full development standards live in [AGENTS.md](AGENTS.md).

## License

[MIT](LICENSE) © Daniel Del Grosso and contributors
