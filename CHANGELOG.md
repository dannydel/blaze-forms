# Changelog

All notable changes to this project are documented in this file. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html) — versions are computed from
git tags by [MinVer](https://github.com/adamralph/minver) (`v*` tag prefix), not hand-edited.

## [Unreleased]

### Added

- **Core foundation**: definition schema (`schemaVersion` 1), serialization via
  `System.Text.Json`, the condition/expression engine, versioning and publish contracts, and
  UI-agnostic host contracts (`IFormDefinitionStore`, `IFormDraftStore`, in-memory
  implementations).
- **Linter and safe Markdown**: `FormLinter` rule engine (errors and warnings, publish-blocking
  on errors) and the shared Markdig pipeline for `help`/`paragraph`/`callout` content — raw HTML
  disabled, images stripped, link protocols allow-listed.
- **Renderer**: `FormRenderer` and `FormSubmissionView` components, default field components,
  neutral CSS token theme, and a MudBlazor sample adapter (`IFieldComponentRegistry`).
- **Designer**: `FormDesigner` and `FormLibrary` components — keyboard-first canvas, palette,
  properties/rule editors, linter dock, preview pane, version history, and publish dialog, with
  cycle detection for conditional logic and an `FormRenderer.Ephemeral` preview path.
- **Calc engine**: schema v2 — computed fields with operations/functions/formats, a dependency
  analyzer, an evaluator with a recompute lifecycle in the Renderer (capture-at-submit,
  `<output>` display), and Designer authoring UI with a sample calculation and E2E a11y coverage.
- **Repeating groups**: schema v3 — `RepeatingRows` answer model, row-scoped conditional logic
  and expressions; Renderer support for fillable rows with per-row logic and accessibility; and
  Designer authoring, drill-in scope, a sample repeating group, and E2E coverage.
- **API hygiene**: components with no public inheritance contract sealed and hidden from
  IntelliSense (`[EditorBrowsable(EditorBrowsableState.Never)]`) to keep each package's
  IntelliSense surface limited to its documented contract types.
- **Published JSON Schema**: `FormJsonSchema.CreateDefinitionSchema()` exports the definition
  format as a draft 2020-12 JSON Schema, golden-pinned like the wire format; the generated file
  is published at `docs/schemas/form-definition-v3.schema.json`, attached to GitHub releases, and
  served from GitHub Pages via a new `pages.yml` workflow. See `docs/schema.md` for consumption
  and the add-only publish policy.
- **Accessibility gate coverage (Increment B)**: axe now scans six previously-unopened Designer
  dialogs/panels — `DeleteProtectionDialog`, `RetireConfirmationDialog`, `VersionHistory`,
  `KeyboardHelpDialog`, `ValidationRuleEditor` (with a real rule and condition), and
  `OptionsEditor` (with option rows) — plus the cycle-detection `role="alert"` path in both
  `VisibilityRuleEditor` and `CalculationEditor` (new `DesignerDialogAccessibilityTests`); the
  library's filtered-to-zero-results empty state, a repeating group at zero rows, a page whose
  only section is empty, and a single field left `aria-invalid` with its own inline message while
  the aggregate error summary is absent (new `EmptyAndErrorStateAccessibilityTests` plus one new
  `LibraryAccessibilityTests` scenario); and, for the first time, the public WASM demo itself
  (`eng/demo-smoke-test.mjs` now runs `@axe-core/playwright` against `/`, `/fill`, `/library`,
  `/design`, and `/submission/{id}` with the same five WCAG 2.2 AA tags the C# gate uses). A
  measured `best-practice` spike (docs/accessibility-statement-plan.md, resolved decision 6),
  scanning every representative scenario in this suite with axe-core's `best-practice` tag alone,
  found **zero hits, full stop** across the gated Renderer/Designer surface.
  `AccessibilityAssertions.AssertNoViolationsAsync` now runs one combined axe scan per scenario
  (the five WCAG 2.2 AA tags plus `best-practice`) and partitions the results on
  `AxeResultItem.Tags`, failing on either bucket with a distinct message — this is a fatal check,
  the same as the AA gate itself, not merely advisory, given the allow-list ships empty; a future
  axe-core upgrade that adds a new best-practice rule with a real hit on a gated page will turn
  the build red, and that hit either gets fixed or gets a one-line-justified allow-list entry.
- **Live WASM demo**: `samples/BlazeForms.Demo.Wasm` — a standalone `Microsoft.NET.Sdk.BlazorWebAssembly`
  app referencing `BlazeForms.Renderer` and `BlazeForms.Designer` directly, sharing only the
  reference enrollment form's data seed with the sample host via a linked compile item so the two
  can never drift. Deployed by `pages.yml` to `/blaze-forms/demo/`; `ci.yml` publishes it on every
  PR so a trim or publish regression surfaces at review time, not at deploy time.
- **High-contrast theme and `forced-colors` hardening** (docs/accessibility-statement-plan.md,
  Increment C): an opt-in `[data-bf-theme="high-contrast"]` token block in `blazeforms.css`
  (`text`/`border` `#000000`, `surface`/`bg` `#ffffff`, `primary` `#0b3d91`, `danger` `#8c0f0f`,
  both `*-contrast` tokens `#ffffff`, `--bf-focus-ring-width` `3px`) where every RENDERER text and
  non-text pair clears **7:1**, computed and pinned by new `ThemeContrastTests` cases — well above
  the 4.5:1/3:1 the shipped default theme itself has to clear. The selector is the BARE attribute
  (not `:root[data-bf-theme=...]`), so the theme also applies scoped to any ancestor of a rendered
  form, not only `<html>`. `@media (prefers-contrast: more)` folds into the identical values,
  guarded against drift by a dedicated test — that fold is a plain `:root`-specificity rule, so a
  host re-declaring `--bf-color-*` and loading after `blazeforms.css` cancels it for that host, by
  design (documented, not treated as "automatic"). No `.razor.css` file was edited to make the
  RENDERER half of the theme work — a color-only token re-declaration is the whole implementation
  there. The DESIGNER half needed one exception: `blazeforms-designer.css` gained its own
  `[data-bf-theme="high-contrast"]` override for `--bf-canvas-row-selected-bg`, because that token
  derives from the renderer's own primary/bg tokens via `color-mix()` and cannot clear even the
  default theme's 3:1 floor under the high-contrast palette at any mix percentage — a real
  token-contract gap this override discloses (new `DesignerContrastTests` cases), not silently
  papers over: `--bf-color-muted` rendered on that background still does not clear 4.5:1, and is
  documented as a known limitation rather than fixed. Separately (and unconditionally, not
  opt-in), new `@media (forced-colors: active)` rules hold three affordances that would otherwise
  rely on color alone distinguishable once the UA replaces every author color: every focus ring
  maps to the `Highlight` system color (matching `:root, [data-bf-theme="high-contrast"]` and
  placed AFTER the opt-in theme block, so opting into the theme while forced colors is active
  cannot silently defeat this hardening); an invalid `.bf-field` input's border maps to `Mark`,
  reinforcing (not replacing) its existing text error message, placed AFTER the unconditional
  danger-border rule it shares a selector with (a media query adds no specificity — source order
  alone decided the original, since-fixed ordering bug); and `BlazeForms.Designer`'s selected
  canvas row — previously a `color-mix()` **background only**, erased entirely under forced
  colors — gains a structural, non-color `border-inline-start` plus the
  `SelectedItem`/`SelectedItemText` system colors (`CanvasNodeRow.razor.css`, the one hardening
  change that did touch component CSS, as the plan's own correctness/marketable split allows).
  New `HighContrastAccessibilityTests` (`BlazeForms.E2E.Tests`) axe-scan `/fill`, `/design`, and
  `/library` under the opt-in theme, and `/fill` and `/design` under `forced-colors: active`
  (consolidated onto as few full app boots as this suite's shared Blazor Server host can afford),
  directly asserting the selected canvas row's computed `border-inline-start-width` under forced
  colors, and a dedicated combined theme-plus-forced-colors case
  (`TheOptInThemeDoesNotDefeatForcedColorsFocusRingHardening`) — never via a click-then-assert-
  focus pattern, per the caution the plan itself records. Every scenario applying the theme or
  forced colors asserts a POSITIVE PRECONDITION (a real computed-style check, not just that the
  enabling API call didn't throw) before scanning — an earlier version of this theme's own tests
  used `Page.AddInitScriptAsync` to set the attribute, which runs before
  `document.documentElement` exists and silently no-ops, so every one of those scans was actually
  scanning the DEFAULT theme; fixed to apply the attribute via `EvaluateAsync` after navigation,
  with the precondition catching any recurrence.
  `samples/BlazeForms.Demo.Wasm`'s `MainLayout` ships a real toggle button for the opt-in theme —
  Blazor-managed state (`_isHighContrast`, re-rendered declaratively into `aria-pressed`), not a
  JS module mutating the DOM behind Blazor's back — with `aria-pressed` as its ONE state signal
  (no redundant "On"/"Off" text double-announcing the same state), in normal document flow rather
  than a fixed overlay (WCAG 2.4.11 Focus Not Obscured), and does not persist across a reload.
  Scanned by `eng/demo-smoke-test.mjs` in both states — a procurement reviewer can click the
  claim, not just read about it. `docs/theming.md` gains a "High contrast" section, including a
  "Known limitations" subsection (no dark high-contrast variant; `prefers-contrast: more`
  force-flips a host's own dark theme to white with no built-in opt-out; radio/checkbox/yes-no/
  date-range fields get no border-based invalid cue in any theme).

### Changed

- **Packaging and release pipeline**: real NuGet metadata (description, tags, project URL,
  packed README, symbol packages) on all three `src/` packages; MinVer-derived versioning from
  `v*` git tags in place of a hand-maintained `<Version>`; a tag-triggered `release.yml` that
  packs, verifies the packed version against the tag, and publishes `.nupkg`/`.snupkg` plus a
  GitHub release; `ci.yml` now packs every PR so packability regressions surface before a tag.

### Fixed

- **Renderer**: Date field draft values now hydrate back to `DateOnly` correctly on resume,
  instead of surfacing as a raw string.
- **Renderer (accessibility, user-visible)**: `--bf-color-border`'s default value changed from
  `#c7c9cd` to `#82878e` — the previous value measured 1.66:1 against `--bf-color-bg` and 1.55:1
  against `--bf-color-surface`, both below the 3:1 WCAG 1.4.11 Non-text Contrast minimum for
  every text input, textarea, and select the shipped theme renders. A host relying on the exact
  previous shade should re-declare `--bf-color-border` explicitly. New computed-contrast tests
  (`ThemeContrastTests`) now pin every color token pair's ratio against this and future changes.
- **Designer (accessibility)**: `FormDesigner`'s three-pane docked layout now collapses to a
  single stacked column (palette → canvas → properties, DOM order unchanged) below a new
  `--bf-breakpoint-dock-collapse` (60rem) breakpoint — the layout previously had no `@media` query
  at all and required two-dimensional scrolling at the 320 CSS px viewport WCAG 1.4.10 Reflow
  requires. New `ReflowAccessibilityTests` gate this and WCAG 1.4.12 Text Spacing at 320×512 for
  `/fill`, `/design`, `/library`, and `/submission/{id}`.
- **WASM demo (accessibility, user-visible)**: the demo home page's inline links
  (`samples/BlazeForms.Demo.Wasm/wwwroot/app.css`) relied on color alone to distinguish them from
  surrounding paragraph text — a real WCAG 1.4.1 Use of Color failure the new demo axe scan (see
  above) caught immediately. Links are now underlined by default, not only on hover/focus. The
  sample host's own `wwwroot/app.css` carried the identical pattern, unreached by any existing
  Playwright suite (nothing scans `/`), and is fixed the same way.
- **`.bf-visually-hidden` is now declared in the GLOBAL `BlazeForms.Renderer/wwwroot/blazeforms.css`**,
  not the CSS-isolated `FormRenderer.razor.css` — previously it compiled to
  `.bf-visually-hidden[b-xxxxxxx]` and applied only to markup `FormRenderer` itself renders, which
  is why `samples/BlazeForms.Demo.Wasm` had to redeclare its own copy in `app.css` (now removed).
  `blazeforms.css` is now documented (`README.md`, `docs/theming.md`) as functionally required —
  not a swappable default theme — precisely because this rule (and the reflow/motion media
  queries already there) has no token equivalent a host could otherwise provide itself.
  `samples/BlazeForms.Sample/Components/Pages/Design.razor` gains the same visually-hidden `<h1>`
  the demo host already had — its `Routes.razor` uses `<FocusOnNavigate Selector="h1">`, and
  `Design.razor` rendered no `<h1>` at all, so focus went nowhere on navigation to `/design` (a
  focus-management defect; 2.4.3 Focus Order is the closest WCAG criterion, though none mandates
  focus movement on SPA navigation specifically). The host-owns-the-`<h1>` contract is now
  documented in `README.md` and `docs/theming.md`, including that the shallowest heading either
  RCL emits is `h2` — not "and never lower", since author content and internal sub-structure do
  go to `h3`/`h4`.
- **Both hosts now link `blazeforms.css`/`blazeforms-designer.css` BEFORE their own `app.css`**,
  not after — same specificity on both sides (a bare class selector), so the load order alone
  previously meant a host re-declaring a `--bf-*` token in `app.css` was silently overridden by
  the RCL's own default, contradicting `docs/theming.md`'s "re-declare these tokens and change
  nothing else" contract (now stated plainly there). `eng/demo-smoke-test.mjs`'s own
  fill-and-submit flow confirms the reorder is safe on the demo.
  The sample host could not take the same fix until its E2E suite gained a real hydration barrier:
  its pages were all `@rendermode InteractiveServer` (prerendering on), and the Playwright + axe
  suite filled fields immediately after its own "wait for the heading" step with no separate wait
  for interactivity — so a fill could land during the STATIC prerendered pass, before the circuit's
  own `@bind` handlers were live to capture it. That race was present **on every run, independent
  of link order**; reordering the links only shifted asset-load timing enough to lose it more often.
  **Fixed:** those five pages now render with `prerender: false`, and `HeadOutlet` carries the
  matching `@rendermode="InteractiveServer"`. That second half is what an earlier attempt was
  missing: a *static* `HeadOutlet` receives nothing from a prerender-skipped page and never updates
  from the circuit either, which is why `prerender: false` alone regressed `<title>`/`<meta>`
  injection into a universal `document-title` violation — a one-line omission in the sample, not the
  framework defect it was first taken for. With prerendering off, the driver's existing
  "wait for the heading" step *is* the interactivity barrier, since the heading only exists once the
  circuit has rendered. Demonstrated closed rather than merely passing: under CDP throttling
  (6× CPU, 400 ms latency) the old code loses the fill and stays on page 1, while the fixed code
  captures it and advances. `SubmissionAccessibilityTests` now asserts its post-submit navigation
  with `Expect(Page).ToHaveURLAsync` instead of `Page.WaitForURLAsync`, because under
  `prerender: false` that transition is same-document and never fires the browser `Load` event the
  latter waits on. The WASM demo's `app.css` also finishes the `--bf-*` →
  `--demo-*` rename for its own chrome variables (`--bf-font-sans`, `--bf-canvas`,
  `--bf-page-max` were still squatting the real token namespace after the earlier partial rename).
- **Fixed two independent, real focus races in three new E2E scenarios**
  (`DesignerDialogAccessibilityTests`), found and corrected across three review passes:
  - Closing `VisibilityRuleEditor`/`CalculationEditor` only arms `PropertiesPanel`'s own one-shot
    post-close focus-restore — the actual `FocusAsync()` JS interop call fires on that component's
    own next render, independent of the dialog's own removal from the DOM. New
    `DesignerDriver.WaitForDialogCloseFocusRestoreAsync` is the barrier.
  - Separately, and regardless of the first barrier: selecting a DIFFERENT canvas row via a click
    (`DesignerCanvas.Activate`, always committing `DesignerFocusIntent.None`) deliberately arms
    `PropertiesPanel`'s own focus-steal to its Label input
    (`PropertiesPanelTests.SelectingANewNodeWithNoFocusIntentMovesFocusToTheLabelInput`) — a
    scenario needing the CANVAS itself to hold focus afterward (to fire `Delete`, which reads the
    roving cursor) was racing that steal. An initial fix (a click-and-retry loop) papered over the
    symptom rather than the cause and was replaced with two deterministic, race-free primitives:
    `DesignerDriver.MoveRovingCursorToRowAsync` (arrow-key roving-cursor movement, which never
    touches the committed selection and so can never arm the steal — used when the next action
    needs canvas focus) and `DesignerDriver.SelectCanvasRowAndWaitForLabelFocusAsync` (a click,
    then an assertion on the product's own real terminal focus state — used when the next action
    needs the properties panel showing that node). The retry loop is gone.
- **Documentation corrections found while chasing the above**: `docs/theming.md` now says
  `.bf-visually-hidden` is used by `FormRenderer`'s own announcer regions specifically (not "the
  RCLs'" -- nothing in `BlazeForms.Designer` uses that class), and states the cascade-order
  specificity tie plainly rather than via an aside about custom-property declarations.
  `samples/BlazeForms.Demo.Wasm/Components/Pages/Design.razor`'s own comment no longer cites the
  now-removed `wwwroot/app.css` copy of `.bf-visually-hidden` as that class's home.
  `CanvasNodeRow`'s own remarks on why a mouse click needs no extra `FocusAsync` call now say
  plainly that this does not mean focus stays on the row -- `PropertiesPanel`'s own focus-steal to
  its Label input is a separate, deliberate reaction to the same click.
  `docs/accessibility-statement-plan.md` gains an open question (#11): activating a canvas row
  moves real DOM focus out of the `role="listbox"` entirely, on both the click and Enter paths,
  deviating from the WAI-ARIA grouped-listbox pattern the canvas otherwise implements --
  deliberate and unit-tested today, left alone this increment, but flagged for Increment D's own
  SR matrix and `KeyboardHelpDialog` to settle, since it is exactly the behavior this increment's
  own new E2E scenarios kept tripping over.

[Unreleased]: https://github.com/dannydel/blaze-forms/commits/main
