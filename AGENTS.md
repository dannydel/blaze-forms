# BlazeForms — development guidelines

Guidance for every contributor, human or agent. Product decisions (scope, schema, phasing, naming of packages and contracts) live in [docs/PRD.md](docs/PRD.md) — consult it before designing anything; this file governs *how* code gets written.

## Invariants

Breaking any of these fails review regardless of how good the feature is:

1. **Agnosticism.** Public contracts of `BlazeForms.Core` and `BlazeForms.Renderer` reference BCL and Microsoft.AspNetCore.Components types only. An architecture test enforces this; extend the test when adding packages.
2. **Schema is a public contract.** Any change to the definition JSON shape bumps `schemaVersion` and lands with round-trip tests and updated golden files.
3. **Immutability.** Nothing may mutate a published definition version — including "harmless" fixes. New behaviour arrives as a new version.
4. **WCAG 2.2 AA.** Every interactive component lands with keyboard and screen-reader acceptance criteria in its PR description, and passes the Playwright + axe CI scan. Keyboard parity: any operation achievable by pointer is achievable by keyboard.
5. **Stable IDs key data.** Field IDs are machine-generated and immutable; labels are display-only and never used as keys.
6. **Author content renders through the safe pipeline.** Markdown-enabled strings (`help`, `paragraph`, `callout` — PRD §5.1) render only via Core's shared Markdig pipeline (raw HTML disabled, protocol allow-list); every other definition or respondent string renders as text. Rendering an author- or respondent-supplied string as raw markup fails review.

## Workflow

1. Write the failing test first — xUnit for Core, bUnit for components. A bug fix starts with the regression test that reproduces it.
2. Implement until green, refactor, keep warnings at zero (`TreatWarningsAsErrors` is on).
3. Update docs and the sample app in the same PR as the feature.
4. Conventional commit messages (`feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`); every commit `Signed-off-by` (DCO).
5. PRs into `main` only; CI green is a merge requirement. SemVer: breaking change ⇒ major.

## C# standards

- .NET naming throughout: `PascalCase` types, members, and constants; `camelCase` locals and parameters; `_camelCase` private fields; `I` prefix on interfaces; `Async` suffix on async methods; no abbreviations in public names.
- `<Nullable>enable</Nullable>`, implicit usings, file-scoped namespaces, latest C# language version, .NET analyzers at `latest-all`.
- Public members carry XML docs, and summaries are always multi-line:

  ```csharp
  /// <summary>
  /// Persists a fill draft so a respondent can resume where they left off.
  /// </summary>
  ```

  Never the single-line form (`/// <summary>Persists…</summary>`).
- Public API changes go through `Microsoft.CodeAnalysis.PublicApiAnalyzers`: update `PublicAPI.Unshipped.txt` in the same commit.
- Prefer records for schema/DTO types, `IReadOnlyList<T>`/`IReadOnlyDictionary<K,V>` on public surfaces, and guard clauses over nested conditionals.
- Core stays trim-compatible (`IsTrimmable`): use System.Text.Json source generation; reflection-based serialization is off-limits there.

## Blazor standards

- **Styles: CSS isolation.** Component styles live in the collocated `.razor.css`, using `--bf-*` tokens for every color, spacing, radius, and font value — hard-coded visual values fail review. Use `::deep` sparingly and comment why. Use CSS logical properties (`margin-inline-start`, not `margin-left`) so RTL works free. The Tailwind v4 layer is for the default theme build only; component CSS speaks tokens.
- **JS interop, module-style.** JS lives in collocated `.razor.js` ES modules, loaded via `IJSRuntime.InvokeAsync<IJSObjectReference>("import", ...)`, disposed in `DisposeAsync` (`IAsyncDisposable`). No globals on `window`, no `eval`, no JS for anything Blazor does natively (class toggling, visibility, focus within the component tree). JS is reserved for genuine platform gaps: drag-and-drop coordinates, `localStorage`, measuring, clipboard.
- **Parameters and events.** `[Parameter]` properties are auto-properties the component never writes to internally; use `EventCallback<T>` (not `Action`) for component events; `[EditorRequired]` on genuinely required parameters.
- **Render discipline.** Keyed loops (`@key`) for node lists; typing in one field must not re-render the whole form — assert render counts with bUnit where regressions are likely; override `ShouldRender` only with a measured justification.
- **Lifecycle & disposal.** Async work in `OnInitializedAsync`/`OnParametersSetAsync`; anything that subscribes, times, or imports JS implements `IAsyncDisposable` and cleans up.

## Testing

- Unit tests are required for all new code: Core logic (expression evaluation, lint rules, versioning, serialization) via xUnit; component behaviour via bUnit; end-to-end designer/renderer flows plus the axe accessibility scan via Playwright in CI.
- Every lint rule ships with tests proving both the violation and the clean case.
- Serialization: golden-file tests pin the JSON of a representative definition per `schemaVersion`.

## Accessibility acceptance criteria (per interactive component)

State in the PR: the tab/arrow-key model, focus destination after each mutating action, what the live region announces, and the axe result. "Works with a mouse" is half a feature.
