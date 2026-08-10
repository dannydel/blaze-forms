# BlazeForms — Product Requirements Document

| | |
|---|---|
| **Status** | Approved — decisions locked via design review, 2026-08-10 |
| **Owner** | Daniel Del Grosso |
| **License** | MIT · copyright "Daniel Del Grosso and contributors" · DCO sign-off |
| **Packages** | `BlazeForms.Core` · `BlazeForms.Renderer` · `BlazeForms.Designer` |
| **Target** | net10.0 (single target through 0.x; multi-targeting evaluated at 1.0) |
| **Design source** | [Claude Design — Form Builder](https://claude.ai/design/p/ee5d2848-4e0a-4b01-8b96-98aff8480a40) (private reference; Tyler-branded assets in it do **not** ship — see §10) |

---

## 1. Vision

BlazeForms is an open-source Razor Class Library family for **defining, publishing, filling, and reviewing versioned forms** in Blazor. A form is data — a serializable definition — not code. Organizations that live on forms (government, education, healthcare intake, HR) get a designer their non-developer staff can use, a renderer their developers can drop into any Blazor app, and an audit trail their compliance people can trust.

Three promises distinguish it:

1. **UI-library agnostic.** No third-party UI types in any public contract. Hosts integrate visually through a documented CSS design-token contract (`--bf-*`), and structurally through a per-field-type component registry. BlazeForms works *with* MudBlazor, FluentUI, Radzen, Bootstrap, Tyler Forge, or plain CSS — it depends on none of them.
2. **Accessibility is the product, not a checkbox.** WCAG 2.2 AA throughout, full keyboard parity for every designer operation, and a built-in publish-gating linter that stops authors from shipping inaccessible forms.
3. **Published means immutable.** Submissions render forever against the exact definition version they were captured with — labels, logic, and all.

## 2. Goals and non-goals

**Goals (P1)**

- A form **designer** component non-developers can operate entirely from the keyboard.
- A form **renderer** component that fills any published version, with conditional logic, validation, drafts, and multi-page progress.
- A **submission view** component that renders captured answers against their captured version.
- A **library** component for browsing/filtering a tenant's forms (thin; trivially replaceable by hosts).
- A versioning model with immutable published versions and lint-gated publishing.
- Host-implemented persistence via small interfaces; the library ships no storage, HTTP, or auth.

**Non-goals (P1)**

- Static SSR (non-interactive) fill. Renderer requires an interactive render mode (Server or WASM); Designer likewise.
- Payment, e-signature, or workflow engines. Submission hand-off is the host's boundary (`IFormSubmissionSink`).
- Multi-language form *content* (see §12 — schema will not preclude it).
- Repeating groups, file upload, lookup fields — schema reserves them; editors and rendering ship in P2 (§13).
- A hosted service, admin portal, or database. BlazeForms is a library, not a platform.

## 3. Personas

| Persona | Uses | Cares about |
|---|---|---|
| **Form author** (program admin, non-developer) | Designer, Library | Building/changing forms without filing a dev ticket; not being able to break accessibility or live data |
| **Respondent** (member of the public) | Renderer | Finishing quickly on any device/AT; never losing a half-completed form |
| **Host developer** | All packages + contracts | Dropping components into an existing app, mapping their design system, owning storage/auth |
| **Reviewer** (staff processing submissions) | SubmissionView | Seeing exactly what the respondent saw, even three versions later |

## 4. Product surfaces

Four Blazor components. Names are the public API.

### 4.1 `FormDesigner`

Three-pane layout: **field palette** (searchable, grouped) · **canvas** (pages → sections → nodes) · **docked properties panel**. Docked is the only P1 layout; overlay and insert-menu variants from the design are recorded as future responsive adaptations, not config options.

- **Canvas**: single tab stop with roving focus. Selection drives the properties panel. Every node row shows label, type chip, required flag, half-width flag, help text, a logic summary chip when a visibility rule exists, and inline lint messages.
- **Reordering — three equivalent paths** (keyboard parity is a hard requirement): drag-and-drop, `Alt+↑/↓` within a section and `Alt+←/→` across sections, and a *Move to position* dialog (`Ctrl+M`) with section + position selects.
- **Keyboard commands** (discoverable via an in-app dialog): `Tab` enter/leave canvas · `↑/↓` move focus · `Enter` open properties · `Ctrl+D` duplicate · `Delete` delete with reference warning · `Ctrl+Z`/`Ctrl+Shift+Z` undo/redo.
- **Undo/redo**: every structural mutation is undoable; history depth 50.
- **Announcements**: every mutation announces to an `aria-live=polite` region in plain language ("Moved to position 3 of 5 in 'Transportation'.").
- **Pages**: tab strip with add-page; sections addable per page; empty page offers *start from template* and *add blank section*.
- **Properties panel**: field ID (immutable, visible), label (blocking-lint when empty), help text, placeholder, required, half-width, options editor (one per line; stored values stable under label edits), min/max for numeric, heading level for headings, visibility-rule summary with add/edit/remove.
- **Delete protection**: deleting a field referenced by logic or validation raises a warning dialog naming every reference; deleting anyway leaves dangling references that the linter reports as blocking.
- **Preview mode**: renders the current draft with live logic and validation inside the designer; test data is discarded on exit.
- **Linter dock** (§8) and **publish dialog** (§7) complete the surface.

### 4.2 `FormRenderer`

- Renders a published definition version: pages as steps with a progress header, sections as `fieldset`/`legend`, fields per the type table (§5).
- **Conditional visibility** evaluated live (§6). Hidden fields are excluded from validation, from the submission payload, and from the accessibility tree.
- **Validation**: on blur per field; on page-advance and submit for the whole page/form. Errors render inline *and* in a focusable `role=alert` summary whose entries are anchor links to the offending field. Messages state the remedy ("Enter a date for 'Date of birth'."), not just the failure.
- **Drafts**: autosaved via `IFormDraftStore`; a returning respondent resumes where they left off. A draft is pinned to the definition version it started on and completes against it, even if a newer version publishes mid-fill.
- **Submission**: values keyed by stable field ID, handed to `IFormSubmissionSink` (§11). Confirmation screen is host-templatable.
- Mobile-first: single column below 480 px (half-width pairs collapse), 44 px minimum touch targets, correct `type`/`inputmode`/`autocomplete` per field type.

### 4.3 `FormSubmissionView`

Read-only rendering of one submission **against its captured definition version**, sectioned as the form was, with label/value rows. Fields hidden by logic at fill time render as "Not applicable — hidden by logic at fill time". A version notice appears when a newer version has since published. JSON export of the envelope.

### 4.4 `FormLibrary`

Thin management surface over `IFormDefinitionStore`: search (name/program/owner), filter (program, status, blocking-issues-only), sort, cards/table toggle, per-form status badge (Published / Draft / Retired), version, submission count, and open-in-designer callback. Hosts with their own list UX skip this component entirely.

## 5. Form definition model

`FormDefinition` → `Page[]` → `Section[]` → `Node[]`. Every node has an **immutable, machine-generated ID**; labels are display-only and never key data. The serialized JSON carries a `schemaVersion` and is a public contract (round-trip + golden-file tested).

**P1 node types (18):**

| Group | Types |
|---|---|
| Text | `text`, `textarea`, `email`, `phone` |
| Numeric | `number`, `currency` (min/max) |
| Date | `date`, `daterange` |
| Choice | `select`, `radio`, `checkboxgroup`, `yesno`, `boolean` |
| Static | `heading` (level 2–4), `paragraph`, `callout`, `divider` |
| Advanced | `calc` — **schema in P1, evaluation engine P2**; renders read-only placeholder until then |

**P2 node types (schema reserved now):** `repeating` (repeating group), `file` (upload), `lookup` (external reference). The designer palette shows them disabled with a phase badge, exactly as the reference design does.

Field-level properties: `label`, `help`, `placeholder`, `required`, `requiredWhenVisible`, `half` (half-width), `options[]` (stable stored values), `min`/`max`, `level`, `visibleWhen` (§6).

### 5.1 Markdown content

Form authors can use Markdown where forms need formatted prose; everything else stays plain text.

- **Markdown-enabled:** `paragraph` and `callout` content, and field `help` text — emphasis, lists, and links cover the real cases (statutory notices, "what you'll need" checklists, links to policy pages).
- **Plain text, always:** `label`, `options[]`, validation messages, and **all respondent input**. Labels feed `legend`/`aria` contexts and are quoted inside error messages; respondent-typed text is rendered as text, never parsed.
- **Safety:** one shared pipeline, defined in Core so the renderer and linter agree — CommonMark via **Markdig** with raw HTML disabled, link protocols allow-listed (`http`, `https`, `mailto`), external links get `rel="noopener noreferrer"`. Definitions are untrusted input (the library supports importing definitions), so sanitization is a correctness requirement, not hardening.
- **Designer:** the properties panel marks Markdown-enabled inputs ("Supports Markdown"); the canvas and preview render the formatted result.
- **Rendered output** uses semantic elements styled by `--bf-*` tokens, so links and lists inherit the host's theme automatically.

## 6. Logic and validation — one expression tree

A single serializable expression model serves visibility now and calc/validation growth later. **No string DSL exists anywhere in P1** (a P2 decision to revisit only if the tree proves too clumsy for calc).

```json
{ "join": "all | any",
  "conditions": [ { "field": "<nodeId>", "op": "is", "value": "Yes" } ] }
```

- **Operators (9):** `is`, `isNot`, `isTrue`, `isFalse`, `isBlank`, `isNotBlank`, `gt`, `lt`, `contains`.
- **Visibility semantics:** hidden ⇒ excluded from validation, submission payload, and the accessibility tree. `requiredWhenVisible` makes a field required only while shown.
- **Cycle detection:** rules are dependency-checked as they are edited; a rule that would create a cycle is rejected with the named path.
- **Cross-field validation rules:** `{ target, message, expression }` using the same tree. Messages must state a remedy (advisory lint A11Y-06 otherwise).
- **Rule editor:** condition rows (the design's Model A — separately labelled Field/Operator/Value rows with an All/Any toggle). Model B (sentence builder) was evaluated and rejected: Model A scales to many clauses and each control carries its own accessible label. *(Resolves the design's OQ-09.)*

## 7. Versioning and lifecycle

- **States:** Draft → Published v1..vN → Retired.
- **Publish** increments the version, requires a change note (kept in version history), and is **gated**: zero blocking lints or the publish dialog lists each blocker with a jump-to-node action.
- **Published versions are immutable — forever.** Edits accumulate on a new draft. Submissions are captured against, and always render against, their version.
- **Retire** stops new fills; existing submissions remain renderable. There is no unpublish and no rollback-in-place — "restoring" an old version means publishing its content as v(N+1). Only never-published drafts can be deleted.
- **Version history** lists every version with note, author, date, and submission count.

## 8. Linter

Runs continuously in the designer; results in a collapsible dock (message, detail with rule ID, jump-to-node or one-click fix) and inline on canvas rows.

- **Blocking** (gate publish): **A11Y-01** input has no label (placeholder is not a label) · **FR-03** rule references a field that no longer exists.
- **Advisory**: **A11Y-06** validation message states no remedy · **A11Y-08** heading level skips a rung · **A11Y-09** Markdown link text does not describe its destination ("click here", bare URL).

Rule IDs are a public registry documented in-repo; rules are pluggable so hosts and contributors add their own (each new rule ships with ID, rationale, and tests).

## 9. Architecture and packages

| Package | Contents | Depends on |
|---|---|---|
| `BlazeForms.Core` | Schema, serialization, expression engine, validation, linter, versioning, safe-Markdown pipeline (§5.1), all host contracts. **No UI.** | BCL + Markdig (the sole third-party dependency; MIT, trim-compatible — lives in Core so renderer and linter share one Markdown policy) |
| `BlazeForms.Renderer` | `FormRenderer`, `FormSubmissionView`, default field components, neutral theme CSS | Core |
| `BlazeForms.Designer` | `FormDesigner`, `FormLibrary`, rule builder, linter dock | Core, Renderer |

**Host contracts** (all in Core; hosts register implementations via DI; an in-memory implementation ships for demos/tests):

```csharp
IFormDefinitionStore   // load/save definitions, list versions, publish, retire
IFormSubmissionSink    // receives the submission envelope; host owns everything after
IFormDraftStore        // save/load fill drafts keyed by (formId, version, respondent key)
IFieldComponentRegistry// optional per-field-type component overrides (§10)
```

**Submission envelope:** `{ submissionId, formId, definitionVersion, startedAt, submittedAt, values: { fieldId: value } }` plus a host-supplied opaque respondent key. Hidden-by-logic fields are **absent**, not null.

**The agnosticism invariant** — no third-party UI type appears in any public contract of Core or Renderer — is enforced by an architecture test, not convention.

## 10. Theming and UI-agnosticism

Two integration layers, independently usable:

1. **Token contract.** Every visual decision in shipped components flows through documented `--bf-*` CSS custom properties (color, typography, spacing, radius, focus, motion). Hosts restyle everything by mapping their system's tokens onto `--bf-*` — a Bootstrap mapping ships as a documentation page to prove the CSS-only path.
2. **Component registry.** `IFieldComponentRegistry` maps field types to host components (`email` → their design system's input). Shipped defaults are semantic plain-HTML components. A **MudBlazor adapter in `samples/`** (not a supported package) is the P1 honesty test for this seam.

**Default theme:** clean and minimal, authored with **Tailwind CSS v4** — compiled at *library* build time into static CSS shipped in the RCL's static web assets, with `@theme` values bridged to `--bf-*`. Consumers never need a Tailwind toolchain; the token contract, not Tailwind, is the public surface. Component-scoped styles use Blazor CSS isolation (see AGENTS.md).

The Tyler design system in the reference design project is proprietary and ships nowhere in this repo. It remains the private downstream validation of the token contract.

## 11. Accessibility requirements (WCAG 2.2 AA)

- Full keyboard parity: every designer arrangement reachable by mouse is reachable from the keyboard (three reorder paths, §4.1).
- Roving-focus canvas (single tab stop); focus is managed explicitly after mutations (delete moves focus to neighbour, insert to the new node) and every mutation is announced via `aria-live=polite`.
- Renderer: labels programmatically associated (never placeholder-as-label), `fieldset`/`legend` for groups, `aria-required`, error summary with `role=alert` + anchor links, correct `autocomplete`/`inputmode`, 44 px touch targets, `prefers-reduced-motion` respected, visible focus ring throughout.
- CI gate: Playwright + axe scans on designer and renderer; per-component keyboard/SR acceptance criteria (see AGENTS.md).

## 12. Localization

- All library chrome (designer UI, renderer strings, linter messages) resolves through `IStringLocalizer` with resx from day 1; English ships, community adds cultures.
- Neutral theme uses CSS logical properties so RTL works without a second stylesheet.
- Form *content* localization (labels/help as culture-keyed maps) is a named P2 feature; P1 schema keeps plain strings but the `schemaVersion` mechanism gives it a migration path.

## 13. Phasing

| | P1 | P2 |
|---|---|---|
| Surfaces | Designer, Renderer, SubmissionView, Library (thin) | Responsive designer layouts (overlay/insert-menu) |
| Field types | 18 (§5); `calc` schema-only | `repeating`, `file`, `lookup`; calc evaluation engine |
| Logic | Visibility + cross-field validation (one tree) | Calc functions (`today()`, date math); string DSL *only if* the tree proves insufficient |
| Content | English chrome, RTL-ready; Markdown in `help`/`paragraph`/`callout` (§5.1) | Multi-language form content |
| Integration | Token contract, registry, MudBlazor sample, Bootstrap mapping doc | Additional adapter samples as community demand shows |

## 14. Success criteria (P1)

1. A host developer goes from `dotnet add package` to a rendered multi-page form with logic in **under 15 minutes** using only the README.
2. A form author builds and publishes the reference enrollment form (3 pages, 2 conditional branches, 1 blocking lint to fix) **without touching a mouse**.
3. Playwright + axe report zero WCAG 2.2 AA violations on designer and renderer in CI.
4. The MudBlazor sample swaps every input component **without any change to Core or Renderer**.
5. A submission captured against v3 renders identically after v4 publishes (golden-file test).

## 15. Decision log

| # | Decision | Choice |
|---|---|---|
| D1 | Document home | `docs/PRD.md`, versioned with code |
| D2 | Packaging | Core / Renderer / Designer; Renderer consumable without Designer |
| D3 | Agnosticism mechanism | Tokens **and** component registry; plain-HTML defaults |
| D4 | Audience | **OSS** (MIT, DCO, personal GitHub) — revised from internal-first |
| D5 | Target | net10.0 only through 0.x; interactive render modes; static SSR out of scope P1 |
| D6 | Persistence | Host-implemented contracts only; in-memory impl for demos; drafts host-side |
| D7 | Scope | Library ships thin; calc = schema P1 / engine P2 |
| D8 | Rule editor (OQ-09) | Model A condition rows; Model B rejected |
| D9 | Designer layout | Docked only in P1 |
| D10 | Name | BlazeForms (NuGet clear as of 2026-08-10; `BlazorForms` is taken and active) |
| D11 | Default theme | Tailwind v4, compiled at library build; tokens are the public contract |
| D12 | Expression model | One serializable tree everywhere; no string DSL in P1 |
| D13 | Drafts | Pinned to starting version; no mid-fill migration |
| D14 | Lifecycle | Publish/retire only; published versions immutable forever |
| D15 | Localization | Chrome localizable P1; content P2; RTL via logical properties |
| D16 | Dev standards | See `AGENTS.md` (canonical); CONTRIBUTING.md points into it |
| D17 | Markdown support | Authors get Markdown in `help`/`paragraph`/`callout` via a shared Markdig pipeline in Core (raw HTML disabled, protocol allow-list); labels, options, and respondent input stay plain text |

## 16. Open questions

- **OQ-1:** Multi-target net8.0 at 1.0? (Revisit with adoption data.)
- **OQ-2:** Draft retention/expiry policy — library default or purely host policy? (Leaning host policy + documented guidance.)
- **OQ-3:** Does `FormLibrary` paginate via the store contract in P1, or is in-memory filtering acceptable at launch scale?
