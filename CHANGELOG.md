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

### Changed

- **Packaging and release pipeline**: real NuGet metadata (description, tags, project URL,
  packed README, symbol packages) on all three `src/` packages; MinVer-derived versioning from
  `v*` git tags in place of a hand-maintained `<Version>`; a tag-triggered `release.yml` that
  packs, verifies the packed version against the tag, and publishes `.nupkg`/`.snupkg` plus a
  GitHub release; `ci.yml` now packs every PR so packability regressions surface before a tag.

### Fixed

- **Renderer**: Date field draft values now hydrate back to `DateOnly` correctly on resume,
  instead of surfacing as a raw string.

[Unreleased]: https://github.com/dannydel/blaze-forms/commits/main
