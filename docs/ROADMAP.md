# BlazeForms roadmap

> Status: **draft for milestone planning**. The 1.0 product boundary is approved: file upload,
> lookup fields, and localized form content must ship before GA. Milestone contents below become
> binding when their feature design plan is approved. Product contracts remain governed by
> [PRD.md](PRD.md).

## North star

BlazeForms 1.0 is the form library a Blazor team can adopt for a real public-facing workflow
without immediately replacing its renderer, authoring experience, persistence seams, or
accessibility model. It remains a library rather than a hosted form platform: hosts own storage,
authorization, tenant boundaries, workflows, and infrastructure.

The 1.0 release must support:

- versioned, immutable definitions and submissions;
- keyboard-first authoring and WCAG 2.2 AA rendering;
- conditional logic, cross-field validation, calculations, and repeating groups;
- files represented by durable host-owned references, never embedded bytes;
- asynchronously searched external lookups with stable captured values;
- author content rendered in a selected culture with an explicit fallback chain; and
- Interactive Server and WebAssembly hosts through the same public contracts.

Milestones are outcome-based. Dates are deliberately omitted until the first preview package has
real adoption data.

## Release train to 1.0

### `0.2.0-preview.1` — publish the product that exists

**Outcome:** a developer can discover, install, and evaluate BlazeForms without cloning the
repository.

- Publish `BlazeForms.Core`, `BlazeForms.Renderer`, and `BlazeForms.Designer` to NuGet.
- Create the first GitHub release with symbols and the published JSON Schema.
- Add an original package icon and verify package metadata on nuget.org.
- Make README, PRD, changelog, demo, and package descriptions agree about the shipped feature set.
- Exercise the README from a blank Interactive Server host and a blank WebAssembly host.
- Create GitHub milestones and product issues; dependency pull requests are not the roadmap.

**Exit criteria:** all three packages install from NuGet, the quick start works without repository
references, and calc/repeating examples work in both sample hosts.

### `0.2.0-preview.2` — project website and development lab

**Outcome:** prospective users can understand BlazeForms quickly, while contributors have one
purpose-built place to exercise every component state during development.

#### Public project website

Replace the current link-only Pages root with a polished, accessible project site. Its job is to
explain and prove the product; it is not the development test harness.

- Lead with the BlazeForms name, its versioned/accessibility-first promise, and clear **Get
  started**, **Live demo**, **Documentation**, and **GitHub** actions.
- Explain the lifecycle visually: define → design → lint → publish → fill → review the captured
  version.
- Show real Renderer and Designer UI rather than generic illustrations or a grid of marketing
  cards.
- Give the major differentiators dedicated, scannable sections: immutable versions, keyboard-first
  authoring, conditional logic, calculations, repeating groups, UI-library agnosticism, safe
  Markdown, theming, and accessibility evidence.
- Publish task-oriented documentation for installation, definitions/schema, rendering, designer
  integration, persistence contracts, theming/adapters, localization, accessibility, migration,
  and troubleshooting.
- Link directly to NuGet packages, the current JSON Schema, release notes, the live WASM demo, and
  GitHub source/issues.
- Source or compile-check code examples so the website cannot advertise stale APIs.
- Meet the same keyboard, reflow, contrast, reduced-motion, and axe expectations as the product;
  add metadata, social previews, canonical URLs, and useful search-engine descriptions.
- Keep the first viewport focused: one strong BlazeForms visual, a short promise, and primary
  actions. Motion supports hierarchy and respects `prefers-reduced-motion`.

**Public-site exit criteria:** a new visitor can answer what BlazeForms does, how it fits into a
Blazor host, why versioning/accessibility matter, and where to start without opening the repository;
all primary paths work on mobile and keyboard; site links and snippets are validated in CI.

#### Development lab

Create a contributor-facing Blazor site—working name **BlazeForms Dev Lab**—with stable scenario
routes for manual exploration, regression reproduction, screenshots, and Playwright. The
implementation plan decides whether to evolve `samples/BlazeForms.Sample` or introduce a separate
dev host; it must not duplicate the same fixtures and host behavior across two projects.

- Catalog every field and surface independently: Renderer, Designer, Library, SubmissionView,
  dialogs, linter states, empty states, and host-component overrides.
- Provide deterministic fixtures for default, validation-error, loading, empty, long-content,
  large-form, high-contrast, RTL, narrow-viewport, and reduced-motion scenarios.
- Add controllable host failures and timing: draft save failure, definition-store failure, slow and
  stale lookup results, upload progress/rejection/cancellation, and submission failure.
- Include controls for culture, theme, viewport preset, artificial latency, and seeded data without
  hiding the component under test behind unnecessary app chrome.
- Show inspectable definition, draft, and submission JSON plus reset/reseed actions. Never expose
  secrets or production data.
- Give each scenario a stable URL and durable test identifier so a bug report can link directly to
  the reproduced state and Playwright can navigate without setup through unrelated UI.
- Keep the reference end-to-end enrollment flow, but separate it from atomic component/state
  scenarios.
- Run a representative Dev Lab smoke and axe suite in CI; use targeted routes for deeper E2E,
  visual, performance, and localization tests as those gates are added.

**Dev-Lab exit criteria:** every supported component and important asynchronous/error state has a
named route; a contributor can reproduce a reported state from a URL; the browser suite consumes
the same fixtures people use manually; the public website and WASM demo contain no development-only
controls.

### `0.3.0-preview` — file upload

**Outcome:** a respondent can attach files without BlazeForms becoming a file-storage product.

#### Contract constraints

- Definition schema v4 describes allowed file count, per-file size, and accepted file types.
- The canonical answer is an ordered collection of immutable upload references plus display
  metadata. File bytes and base64 never enter a draft or submission envelope.
- The storage key is opaque and host-issued. Client-supplied names and content types are retained
  only as untrusted display metadata.
- Renderer code opens each browser file with a library-enforced maximum and streams it directly to
  the host implementation. It never buffers a whole upload merely for convenience.
- Drafts retain staged references so a resumed fill does not upload the same file again. Hosts own
  abandoned-upload retention, malware scanning, authorization, and final persistence.
- A completed submission contains only accepted upload references; incomplete or rejected uploads
  block submission.

#### Product slice

- Core answer model, schema v4, source-generated serialization, golden files, JSON Schema, and
  upload-related lint rules.
- A small host contract for staged upload, removal, and status; no filesystem, cloud, or database
  implementation in a supported package.
- Renderer selection, streaming progress, cancellation, retry, removal, validation, draft resume,
  repeating-group support, and submission capture.
- Designer controls for count, size, accepted types, required behavior, and accessible help text.
- Server sample implementation and a documented WebAssembly-over-HTTP implementation pattern.
- Keyboard, screen-reader, failure-state, size-limit, filename, and cancellation tests.

**Exit criteria:** no unbounded read path exists; no client filename is used as a storage name; a
draft round-trip preserves upload references; file operations are keyboard accessible; Server and
WebAssembly integration paths are documented and tested.

### `0.4.0-preview` — lookup fields

**Outcome:** a form can select durable references from host-owned datasets without embedding
endpoints, credentials, or large option lists in its definition.

#### Contract constraints

- Definition schema v5 identifies a host-defined source key and lookup behavior, never a URL or
  credential.
- The provider contract is asynchronous, cancellable, and paged. It supports search and resolving
  captured values during draft resume and submission review.
- A captured answer includes the stable external value and the label shown at fill time. Historical
  rendering never silently substitutes a renamed current label.
- Provider failures are recoverable UI states and never erase an existing selection.
- Requests debounce, cancel superseded work, and keep network latency off the render path.

#### Product slice

- Core lookup definition/answer types, schema v5, serialization, golden files, JSON Schema, and
  boundary linting.
- Host lookup-provider contract with deterministic paging and cancellation semantics.
- Accessible combobox/listbox renderer with loading, empty, error, retry, and resolved-selection
  states.
- Designer source-key and selection-behavior properties with preview fixtures.
- Row-scoped lookup values inside repeating groups and submission-view label snapshots.
- Tests for keyboard behavior, stale-response suppression, cancellation, paging, draft resume,
  provider failure, and large datasets.

**Exit criteria:** the definition contains no infrastructure details; a renamed external record
does not change a historical submission; superseded searches cannot overwrite newer results; the
combobox passes the browser accessibility gate.

### `0.5.0-preview` — localized form content

**Outcome:** one immutable definition version can serve respondents in multiple cultures while
preserving exactly which content a submission used.

#### Contract constraints

- Definition schema v6 keeps today's strings as default content and adds culture-keyed overlays.
  Existing v1-v5 definitions remain valid and require no destructive migration.
- Translations key pages, sections, nodes, and repeating groups by immutable IDs; option labels by
  stable option value. Validation rules gain stable IDs before their messages become translatable.
- Cultures use BCP 47 names with a deterministic fallback chain: exact culture, parent culture,
  then the definition's default content.
- Drafts and submissions capture the selected culture. Submission review defaults to the culture
  used at fill time, with an explicit reviewer override.
- The renderer emits an appropriate `lang` and `dir` boundary for form content. The host remains
  responsible for the document-level language.
- Markdown safety is identical in every culture; translations do not create a second rendering
  path.

#### Product slice

- Translation overlay model, culture resolver, schema v6, golden files, JSON Schema, and public
  APIs for explicit or current-UI-culture selection.
- Renderer and submission view localization, including localized validation labels/messages,
  culture-aware formatting, RTL, drafts, and culture capture in the envelope.
- Designer culture switcher, translation completeness view, per-culture preview, and publish
  gating for declared required cultures.
- A documented chrome-localization extension point and at least one verified non-English reference
  experience, so localized author content is not surrounded by English-only controls.
- Reference form translated into a left-to-right culture plus an RTL test culture.

**Exit criteria:** changing culture never changes stored option/lookup values; fallback behavior is
golden-pinned; a submission re-renders in its captured culture; the translated reference form
passes keyboard, reflow, screen-reader, and axe coverage in both text directions.

### `0.6.0-preview` — 1.0 product hardening

**Outcome:** the complete feature set is operable on real forms, not only isolated demos.

- Run external-pilot forms through authoring, publishing, filling, drafts, and review.
- Establish performance budgets for representative 100-field, conditional, repeating, lookup, and
  file-heavy forms; add regression gates where measurements are stable.
- Audit cancellation, disposal, error recovery, retry behavior, and render counts across every
  asynchronous component.
- Resolve draft-retention guidance and `FormLibrary` pagination before their contracts freeze.
- Fix the component-registry trimming annotations and prove trimmed WebAssembly publishing.
- Settle the Designer canvas activation/focus model through keyboard and screen-reader testing.
- Complete the public API review and remove accidental surface before it becomes permanent.

**Exit criteria:** no known data-loss bug; no unresolved P1/P2 product question affecting public
contracts; representative pilot forms meet the agreed performance budgets.

### `0.9.0-rc.1` — contract freeze

**Outcome:** consumers can test the exact 1.0 API and schema.

- Move the supported contract into each `PublicAPI.Shipped.txt`.
- Freeze definition schema v6 and publish compatibility expectations for v1-v6.
- Complete migration, hosting, security, theming, localization, and troubleshooting documentation.
- Run the full NVDA/Chrome and VoiceOver/Safari matrix and check in the dated results.
- Publish the versioned accessibility statement and ACR-lite assessment.
- Permit only bug fixes, documentation corrections, and demonstrated accessibility fixes.

**Exit criteria:** at least one external host upgrades from the final preview without source
changes; every documented example compiles; all CI, schema, trim, package, accessibility, and
manual release gates pass.

### `1.0.0` — stable release

**Outcome:** BlazeForms makes a SemVer-backed compatibility promise.

- Publish signed-off packages, symbols, schemas, release notes, and accessibility evidence.
- Support `net10.0` only. Older target frameworks are not added at the point they are leaving
  support; future targeting follows supported LTS releases and adoption evidence.
- Treat public API or wire incompatibility after GA as a major-version event.

## After 1.0

The first post-GA work should deepen the product rather than turn it into a hosted platform:

1. **Advanced calculations:** cross-row `sum`/`count`, richer date/string functions, and clearer
   dependency visualization while retaining the serializable expression tree.
2. **Authoring at scale:** templates, clone/import/export, visual version diff, bulk option import,
   responsive Designer layouts, and paged library browsing.
3. **Adoption kits:** production persistence sample, deployment cookbook, and additional UI-library
   adapters. These remain examples or separately versioned adapters, not dependencies of Core.
4. **Evidence-led compatibility:** add target frameworks only when supported-user demand justifies
   their ongoing test and servicing cost.

## 1.0 guardrails

- No database, authentication system, tenant model, workflow engine, payment processing, or
  e-signature implementation in the three core packages.
- No arbitrary JavaScript execution or string expression DSL in definitions.
- No raw file bytes in definition, draft, or submission JSON.
- No lookup endpoint or credential in a definition.
- No translation scheme that replaces stable IDs or stored option values with display text.
- No accessibility claim without named automated evidence and a dated manual test record.
- No 1.0 API freeze until file, lookup, and localization contracts have survived preview use.
- No public marketing/demo surface overloaded with contributor-only failure injection or test
  controls; the project website, live demo, and Dev Lab have separate jobs.

## Success measures

- A new host renders and submits the reference form from NuGet in under 15 minutes.
- A form author publishes the localized reference form, including a lookup and file field, without
  touching a mouse.
- A respondent resumes a draft containing repeating rows, lookup selections, and staged upload
  references without data loss.
- A reviewer sees the exact labels, lookup display snapshots, files, and culture captured at fill
  time after a later definition version publishes.
- Default Renderer and Designer surfaces pass the automated WCAG 2.2 AA gate and the release's
  required manual screen-reader matrix.
- Interactive Server and trimmed WebAssembly hosts pass the same contract suite.
- The public website sends users to working documentation, NuGet, the live demo, and GitHub, while
  every supported component state is directly reproducible in the Dev Lab.
