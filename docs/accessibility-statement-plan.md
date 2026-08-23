# Accessibility Statement & Conformance — Implementation Plan

> Status: **proposed**. Turns BlazeForms' existing accessibility work into a published, falsifiable
> claim — and closes the gaps a public claim would expose. No schema change (`schemaVersion` stays
> 3); no public API change. Ordered so that **every gap that would falsify the claim is fixed before
> the claim is published**: the statement is the last increment, not the first. Competitive context:
> formengine.io documents no accessibility claims at all; SurveyJS is the only comparable product
> with a published conformance statement and a high-contrast theme. This is the project's headline
> differentiator, so the bar is "a hostile auditor cannot falsify it", not "we shipped a doc".
> Modeled on `docs/adoption-infrastructure-plan.md`.

## The two findings that make this slice necessary

Both were found by reading the CSS, both are **invisible to the axe CI gate**, and both were
independently re-verified before this plan was written.

1. **`--bf-color-border: #c7c9cd` fails WCAG 1.4.11 Non-text Contrast (AA).** Measured **1.66:1**
   on `--bf-color-bg` and **1.55:1** on `--bf-color-surface`; the criterion requires **3:1** for the
   visual boundary of a user-interface component. That border is the only thing identifying every
   text input, textarea, and select (`src/BlazeForms.Renderer/wwwroot/blazeforms.css:22,116`).
   axe-core has no automated rule for component-boundary contrast, so the gate is green while the
   shipped default theme fails an AA criterion on every field in the library.
2. **`FormDesigner`'s three-pane grid fails WCAG 1.4.10 Reflow (AA).**
   `src/BlazeForms.Designer/FormDesigner.razor.css:63` is
   `grid-template-columns: var(--bf-palette-width) minmax(0,1fr) var(--bf-properties-width)` —
   16rem + 20rem = **576px of irreducible width** — and that file contains **no `@media` query at
   all**. At the 320 CSS px viewport 1.4.10 mandates (1280px at 400% zoom), the designer requires
   two-dimensional scrolling. axe cannot see this either, because every scan runs at the default
   1280×720 (`tests/BlazeForms.E2E.Tests/BrowserFixture.cs:30`, `E2ETestBase.cs:36`).

This is structural, not coincidental: automated tooling covers roughly a third of WCAG, and a team
that gates on axe drifts toward conformance-where-axe-looks. The response is not more axe — it is a
computed contrast test, viewport tests, and a manual screen-reader matrix.

## Scope decisions — what was cut as theater

- **A `prefers-reduced-motion` Playwright test — cut.** The entire shipped surface has five
  transitions and all five animate `border-color` only. There is no motion to reduce, and 2.3.3
  Animation from Interactions is **AAA**, out of scope for an AA claim. The existing string
  assertion (`ThemeCssTests.ZerosMotionUnderReducedMotionPreference`) is proportionate.
- **A full VPAT®/ACR — cut, replaced with an "ACR-lite" appendix** (resolved decision 3).
- **The "44px touch target" claim as currently worded — corrected, not tested.** WCAG 2.2 **2.5.8
  Target Size (Minimum) is 24×24 CSS px at AA**; 44px is **2.5.5 Enhanced, AAA**.
  `docs/theming.md:52` claims "44px (WCAG 2.2 AA, PRD §11)", which is wrong about which criterion
  applies — exactly the kind of error a hostile reader uses to discredit the whole document. Keep
  the 44px value (deliberately exceeding AA is good, and worth saying so), fix the citation. Note
  `target-size` is already gated: it is `wcag22aa`-tagged and that tag is in the list, so this is
  not "asserted only in docs".

## Resolved decisions

1. **High-contrast mechanism — an opt-in `[data-bf-theme="high-contrast"]` token block *and*
   `@media (forced-colors: active)` hardening.** They solve different problems.
   `forced-colors` is the *correctness* fix and is not optional once a claim is published: under
   forced colors the designer's selected-row indicator
   (`--bf-canvas-row-selected-bg`, a `color-mix` **background**) is erased by the UA, leaving the
   roving-focus selection invisible for exactly the users forced colors exists for. The
   `data-bf-theme` block is the *marketable* artifact and doubles as the honesty test for the token
   contract: if a high-contrast theme can be expressed purely as re-declared `--bf-*` values with
   **zero `.razor.css` edits**, the contract in `docs/theming.md` is real. Rejected: a separate
   `blazeforms-high-contrast.css`, which duplicates the token block and creates the second source of
   truth `ThemeCssTests` exists to prevent. `prefers-contrast: more` folds into the same values
   rather than becoming a third code path.
2. **Statement home — `docs/accessibility.md` as the single source**, rendered onto the Pages site at
   `/accessibility/`. No hand-authored second HTML copy (a drift surface, and it would be the one
   page on the site not accessible-by-construction). See open question 10 on the rendering machinery.
3. **ACR — "ACR-lite", not a VPAT.** Government and education buyers do ask for one, and FormEngine
   having nothing makes it a differentiator; but the full ITI template is four chapters of mostly
   `Not Applicable` boilerplate with a document lifecycle a one-maintainer repo will not sustain past
   the second release. Ship **one table**: WCAG 2.2 Level A + AA criteria only, standard ITI
   vocabulary (`Supports` / `Partially Supports` / `Does Not Support` / `Not Applicable`), a Remarks
   column, and **separate columns for Renderer and Designer** because their conformance genuinely
   differs. Above the table, prominently: this is a **self-assessment, not a third-party audit, and
   not a VPAT®** (that trademark requires the ITI template); buyers needing an audited ACR should
   treat it as input to their own evaluation. That sentence is what makes the document defensible.
   State the evaluation method explicitly — a conformance table without a methods section is
   worthless to a reviewer. Verify the current VPAT edition number against itic.org before citing one.
4. **Manual SR gate — tiered.** Required and blocking per **minor/major** release: **NVDA + Chrome
   (Windows)** and **VoiceOver + Safari (macOS)** — two screen readers, two engines, free to
   everyone. **JAWS is best-effort and must never appear in the tested-configurations table unless
   someone actually ran it** (listing it because it's expected is the easiest lie for an auditor to
   catch). Per **patch** release: only the flows touching changed components, driven off the PR's own
   accessibility acceptance criteria, which `AGENTS.md` already requires — this makes them cash out.
   The artifact is a dated, checked-in run record at `docs/accessibility/sr-runs/<version>.md`
   generated from `docs/accessibility/sr-test-matrix.md`; **the record, not the template, is what the
   statement cites.** A 3-SR × 4-component matrix per patch release would be rubber-stamped by
   release three, and a rubber-stamped checklist is worse than none.
5. **Statement versioning — explicit and gated.** The statement carries
   `Assessed against: vX.Y.Z (commit <sha>), <date>` plus the axe-core version and browser build,
   and `release.yml` **fails a minor/major tag** whose version doesn't match that line. Patch tags
   are exempt, and the statement says so. Rejected: deriving it from MinVer at build time (the claim
   would auto-update to versions nobody assessed — actively dishonest) and leaving it manual
   (guaranteed to rot).
6. **`best-practice` axe tag — measure first, then add as a separate non-fatal signal.** Do not
   blind-add it to `Wcag22AaTags`; that conflates "we fail an AA criterion" with "axe has an
   opinion". Spike the violation list, then add `AssertNoBestPracticeRegressionsAsync` with a
   commented allow-list, one justification per entry. This keeps the AA gate meaning exactly one
   thing.
7. **Headings — ship a global `.bf-visually-hidden`, document the contract, emit no `h1`, defer a
   `HeadingLevel` parameter.** Neither RCL emitting an `h1` is **correct** for a library (the host
   owns the document `h1`), but the contract is undocumented and the host the entire axe claim rests
   on violates it: `samples/BlazeForms.Sample/Components/Routes.razor:4` uses
   `<FocusOnNavigate Selector="h1" />` and its `Design.razor` has no `h1`, so navigating to
   `/design` moves focus **nowhere** — a silent 2.4.3 regression the gate can't see because
   `page-has-heading-one` is `best-practice`-tagged. Worse, the fix isn't available to hosts:
   `.bf-visually-hidden` is declared in the **CSS-isolated** `FormRenderer.razor.css:57`, so it
   compiles to `.bf-visually-hidden[b-xxxxxxx]` and applies only to markup `FormRenderer` itself
   renders — which is exactly why the WASM demo had to re-declare it in its own `app.css`. Every
   host doing the right thing currently reinvents it. A `HeadingLevel` parameter is the more flexible
   answer for hosts embedding under an `h3`, but it is public API surface, it multiplies
   `HeadingBlock`'s level arithmetic and `HeadingLevelRule`'s assumptions, and nobody has asked —
   defer it and disclose the fixed level as a documented constraint.

## Ground truth (verified in code before planning)

- **What the gate actually scans** — eight suites, all against `samples/BlazeForms.Sample` via
  `SampleAppFixture`, headless Chromium, default 1280×720. Coverage is better than feared:
  `DesignAccessibilityTests.cs` already scans the move dialog, visibility rule editor, calculation
  editor, drill-in scope, linter dock, publish dialog with a blocker, and the preview pane;
  `FillAccessibilityTests.cs` covers conditional reveal, the error summary, calc, and repeating row
  add/fill/reorder/remove.
- **What is not scanned** — the library's empty state (`LibraryAccessibilityTests.cs:27-30`
  deliberately depends on the seeded form); a repeating group at zero rows; an empty section; a
  single field in `aria-invalid` + inline-message state without the summary; four designer dialogs
  never opened by any scan (`DeleteProtectionDialog`, `RetireConfirmationDialog`, `VersionHistory`,
  `KeyboardHelpDialog`) plus `ValidationRuleEditor` and `OptionsEditor`; the cycle-detection
  `role="alert"` paths; **`samples/BlazeForms.Demo.Wasm` — zero a11y coverage** (`eng/demo-smoke-test.mjs`
  contains no axe), which is the surface the public and procurement reviewers actually visit; and any
  viewport other than 1280×720.
- **axe configuration** — `AccessibilityAssertions.cs:18-25` runs five tags by name (`wcag2a`,
  `wcag2aa`, `wcag21a`, `wcag21aa`, `wcag22aa`); no rule-level enable/disable; `best-practice`
  excluded, so `page-has-heading-one`, `heading-order`, `region`, and `landmark-*` are ungated.
- **Token contrast, measured** — `text/bg` 17.35:1, `muted/bg` 6.42:1, `muted/surface` 5.99:1,
  `primary/bg` 6.57:1, `danger/bg` 6.54:1, `focus-ring/bg` 6.57:1 all pass; **`border/bg` 1.66:1 and
  `border/surface` 1.55:1 fail 1.4.11**. The proposed replacement `#82878e` measures 3.62:1 and
  3.38:1 — passing with margin against a host that darkens `--bf-color-surface`.
  `--bf-canvas-row-selected-bg` is a `color-mix` background, i.e. a background-only selection
  indicator.
- **Focus management and live regions are genuinely thorough** — 46 `FocusAsync` call sites across
  the two RCLs, each with a documented destination. Renderer: page heading on step change, error
  summary on submit, confirmation, per-row focus after repeating mutations; three separate
  visually-hidden `aria-live="polite"` regions (`FormRenderer.razor:32-34`) with
  `CalcField.razor:6` setting `aria-live="off"` on the `<output>` so the shared announcer owns the
  speech. Designer: `AriaLiveRegion` fed by `DesignerEditContext`, every dialog focusing a documented
  first element and restoring on close, cycle alerts with their own `tabindex="-1"` + focus, roving
  focus on the canvas.
- **`prefers-reduced-motion`** appears once (`blazeforms.css:65-70`), zeroing a token every
  transition uses — so it does apply library-wide. **`forced-colors`, `prefers-contrast`, and
  `-ms-high-contrast`: zero occurrences in `src/` or `samples/`.** There is no high-contrast story today.
- **Existing unit-level a11y coverage is dense** (~150 `aria-*`/`role=`/`autocomplete`/`inputmode`
  assertions), but the two structural guards are **Renderer-only**: `ThemeCssTests.cs` (30 tokens,
  reduced-motion, focus ring) and `ComponentCssTokenGuardTests.cs:65-72` (no hex/`rgb()` literals in
  `.razor.css`). The Designer's 7 extra tokens in `blazeforms-designer.css:17-48` and its component
  CSS have **no equivalent coverage** — clean today, unenforced tomorrow.
- Host chrome: `<main class="bf-page">` landmark in both hosts; no repeated navigation, so no skip
  link needed. The demo's `app.css:9-17` squats the `--bf-*` namespace with unrelated `--bf-ink`,
  `--bf-accent` etc. — no hard collision today, but the public reference teaches the wrong convention.

## Phased plan — 5 increments, dependency-ordered

### Increment A — Close the two falsifiable AA gaps (no dependencies; must land first)

Nothing downstream is honest until this ships.

- **A1 Fix `--bf-color-border` (1.4.11).** `blazeforms.css:22` → `#82878e`. New
  `tests/BlazeForms.Renderer.Tests/ThemeContrastTests.cs` parses the `:root` token values out of the
  stylesheet and **computes** WCAG relative-luminance ratios in C#: 4.5:1 for text/muted/primary/
  danger against both `bg` and `surface`, 4.5:1 for the `*-contrast` pairs, **3:1 for `border` and
  `focus-ring`**. Expose the ratio helper as `public static` (with a justification — `BlazeForms.Designer.Tests`
  reuses it via a project reference to `Renderer.Tests`, mirroring the source-side dependency) so
  Increment C reuses it verbatim — one contrast module, two callers so far, a third to come.
  `docs/theming.md` gains a "Contrast guarantees" subsection
  (so a re-theming host knows what it is on the hook for) and the 2.5.8-vs-2.5.5 correction.
  *Verify:* the new test fails before the CSS edit and passes after; E2E stays green (border color
  has no layout effect); note the visible default-theme change in `CHANGELOG.md`.
- **A2 Fix designer reflow (1.4.10).** New `--bf-breakpoint-dock-collapse` token; a `@media` query in
  `FormDesigner.razor.css` collapsing the grid to `1fr` and stacking panes in DOM order (palette →
  canvas → properties), following the literal-must-match-token comment pattern already in
  `blazeforms.css:215-222`; check `--bf-dock-height` doesn't create a scroll trap when stacked. New
  `tests/BlazeForms.Designer.Tests/DesignerThemeCssTests.cs` pins all 7 designer tokens (today: zero
  coverage). New `tests/BlazeForms.E2E.Tests/ReflowAccessibilityTests.cs` sets a **320×512** viewport
  for `/fill`, `/design`, `/library`, `/submission/{id}`, asserts no horizontal scroll
  (`scrollWidth <= clientWidth + 1`), and re-runs the axe assertion at that viewport (`target-size`
  and `color-contrast` results can differ when layout changes). Add a
  `private protected virtual ContextOptions` seam to `E2ETestBase` rather than duplicating the fixture.
  *Verify:* the suite fails on `/design` before the media query and passes after; **run it against
  `/fill` and `/library` first** — if the renderer's existing `bf-row` collapse doesn't already
  handle 320px, that is more work inside this increment.
- **A3 Text spacing (1.4.12).** Inject the WCAG override (`line-height: 1.5em`,
  `letter-spacing: 0.12em`, `word-spacing: 0.16em`, paragraph `margin-bottom: 2em`) via
  `AddStyleTagAsync`, then assert no clipping on representative labels, buttons, and canvas rows and
  re-run axe. Likely already passes given token-based spacing and `min-block-size` over fixed
  `height` — but "likely passes" is what a published claim cannot rest on.
- **A4 Guard parity for the Designer's CSS.** Generalize `ComponentCssTokenGuardTests` to cover
  `src/BlazeForms.Designer` (clean today; Increment C is about to add color rules).

### Increment B — Widen the gate's coverage (depends on A2's viewport seam)

Every item is a state axe *can* judge that nobody has pointed it at. **Expect this increment to find
things; budget fixes inside it rather than deferring them into Increment E.**

- **B1** Scan the six unscanned designer surfaces and both cycle-detection `role="alert"` paths
  (Core already detects cycles — provoke one). Extend `DesignerDriver.cs` with the openers; consider
  a sibling `DesignerDialogAccessibilityTests.cs` since `DesignAccessibilityTests.cs` is already 385 lines.
- **B2** Scan the empty and error states: library empty state (decide whether a seedless route or a
  fixture affordance is cheaper, given the current scan deliberately depends on the seed), a
  repeating group at zero rows, an empty section, and a single `aria-invalid` field with its inline
  message and no summary.
- **B3** Promote `.bf-visually-hidden` into the global `blazeforms.css` (per resolved decision 7),
  add the missing `h1` to the sample's `Design.razor` to match the demo, document the
  host-`h1`/`FocusOnNavigate` contract in `README.md`, and assert the global rule exists in
  `ThemeCssTests`. Optionally rename the demo's squatted `--bf-*` variables to `--demo-*`.
- **B4** Spike `best-practice`, then add `AssertNoBestPracticeRegressionsAsync` with a justified
  allow-list. `page-has-heading-one` should leave that list via B3, not live on it.
- **B5** Axe the WASM demo: `@axe-core/playwright` in `eng/demo-smoke-test.mjs` using the **same five
  tags** as the C# gate (cross-reference the two lists in each other's comments — real drift risk
  across the C#/Node boundary), scanning the pages the existing flow already visits. This makes the
  public-facing surface a gated surface, which is a precondition for pointing the statement at it.

### Increment C — High-contrast theme (depends on A1's contrast helper)

- **C1 `forced-colors` hardening.** Focus-ring outlines → system `Highlight`; keep the
  `aria-invalid` boundary distinguishable (a color-only error indicator is erased under forced
  colors — 1.4.1 territory); give the designer's selected row a **non-color** cue (outline,
  `border-inline-start`, or a `::before` marker); check `InlineLintMarker` isn't color-only. Skip the
  drag drop-indicator — pointer-only sugar, and keyboard parity already covers it.
- **C2 Opt-in `[data-bf-theme="high-contrast"]` token block**, color tokens only, all pairs **≥7:1**
  (a high-contrast theme that merely scrapes AA is pointless): `text #000000`, `border #000000`,
  `primary #0b3d91`, `danger #8c0f0f`, `surface #ffffff`, `*-contrast #ffffff`, and
  `--bf-focus-ring-width: 3px`. **Constraint: no `.razor.css` file may be edited** — if one must be,
  that is a token-contract hole worth writing down.
- **C3 Tests.** Extend `ThemeContrastTests` to parse the high-contrast block and assert ≥7:1 (same
  helper, second caller). New E2E scenarios: set the attribute on `<html>` and re-scan; separately
  `EmulateMediaAsync(ForcedColors.Active)` + axe + an assertion that the designer's selected row is
  still distinguishable (computed `outline-style !== 'none'`).
- **C4 Docs + demo.** A "High contrast" section in `docs/theming.md`, and a **theme toggle in the
  WASM demo** — a clickable claim is worth more to a procurement reviewer than a paragraph.

### Increment D — Manual SR test matrix (independent of A–C; can run in parallel)

- **D1 `docs/accessibility/sr-test-matrix.md`** — the template, organized **by flow, not by
  component**, because that is how failures appear. Renderer: page-change announcement,
  submit-with-errors → summary announcement and focus, conditional field appearing/disappearing
  (DOM removal — verify nothing announces a stale reference), **calc recomputation** (the
  `aria-live="off"` `<output>` plus shared announcer pairing is the highest-risk thing in the
  codebase for double- or non-announcement), repeating row add/remove/reorder, confirmation.
  Designer: roving-focus traversal, all three reorder paths, drill-in/out, every dialog's
  open/focus/restore, linter jump-to-node, autosave-failure announcement, publish. Plus the
  submission view and the library's result-count live region. Columns: expected announcement, actual,
  pass/fail, notes — seeded from the acceptance criteria `AGENTS.md` already demands per PR.
- **D2 Run it once, now, and record the result** at `docs/accessibility/sr-runs/<version>.md`.
  **This is the increment's real deliverable** — a template with no run behind it does not license a
  conformance claim. A run record with zero findings across ~25 flows should be treated as evidence
  the run wasn't real.
- **D3** Wire the tiered gate into `CONTRIBUTING.md`, the PR template, and the release checklist.

### Increment E — Publish the statement (depends on A–D; claim only what's verified)

- **E1 `docs/accessibility.md`** — summary claim; scope (packages, versions, components, and
  explicitly what's out: MudBlazor/`/fill-mud`, host chrome, author-authored content); how it's
  verified (axe-core version, the five tags, the scanned state inventory, viewports including 320px,
  keyboard-parity tests, the SR run record, unit guards); **known limitations** (anything A2 didn't
  fix, the fixed `h2` level, JAWS untested, and the honest note that automated scanning covers a
  minority of criteria); host responsibilities (the `h1` contract, the token contrast floor, the two
  stylesheets); how to report an accessibility bug, with a response window you can actually hold;
  the assessed-against block; and the ACR-lite appendix.
- **E2 Surfaces.** Render to `/accessibility/` via `pages.yml`; link from `README.md` near the top,
  `docs/pages/index.html`, `CONTRIBUTING.md`, `AGENTS.md` invariant #4, and the demo.
- **E3 Anti-rot.** The `release.yml` version gate (resolved decision 5) and "update the statement" on
  the release checklist. Consider extending `ThemeContrastTests` to assert the statement's **quoted
  contrast numbers** match the CSS, so the document cannot lie about its own numbers. Skip a
  PR-level "statement touched?" check — over-engineering; the release gate is load-bearing.
- **E4** `CHANGELOG.md`; `AGENTS.md` invariant #4 referencing the statement and matrix; a dated
  **amendment** to PRD §11 for the reflow/text-spacing/high-contrast requirements (the PRD is the
  locked spec — amend, don't edit in place).
- *Verify:* every claim traces to a named test file or a dated run record. Do a hostile read-through:
  for each sentence, ask "what would I show someone who says this is false?" Anything without an
  answer is cut or moved to Known Limitations.

## Risks & open questions

1. **The honesty risk is the whole risk, and it concentrates where axe cannot look.** Both findings
   above are invisible to CI. Publishing before Increment A completes converts a strong position into
   a liability.
2. **Maintenance, honestly costed.** Per minor release: the two-SR run (a focused half day scoped to
   changed components; a full day for a complete pass), the assessed-version bump, and any new
   component's ACR row. Per PR: nothing new. The failure mode is not the work — it is a maintainer
   who hits the version gate and edits the assessed-version line without running anything. The gate
   cannot detect that; only the discipline of citing a **dated run record** can, which is why D2
   matters more than D1.
3. **Should the designer's reflow limitation be disclosed up front?** If A2 lands, no — it's fixed.
   If A2 is deferred (PRD D9 makes docked the only P1 layout, so there is a defensible product
   argument), then **yes, prominently, in the summary claim itself**: "the Renderer meets WCAG 2.2
   AA; the Designer meets it except 1.4.10 Reflow below X px." A split claim is credible; a blanket
   claim with a buried footnote is what invites a bad-faith reading. **This is the human decision
   this plan most needs.**
4. **Limitations to disclose regardless:** the fixed `h2` level and the host's `h1` obligation;
   MudBlazor explicitly out of contract; JAWS untested; and author-authored content conformance as a
   shared responsibility (the linter helps, but an author can still publish an inaccessible form —
   `A11yLabelRule` only blocks the missing-label case).
5. **The `best-practice` allow-list is where honest exceptions go to become permanent.** Require a
   justification per entry and put "review the allow-list" on the release checklist — or don't add
   the tag. A stale allow-list is worse than no signal.
6. **Cross-boundary drift** between the C# and Node axe tag lists (B5). Cross-reference in comments;
   don't abstract for two consumers.
7. **The token contract structurally undermines the claim, and saying so is stronger than
   pretending otherwise.** A host that re-declares `--bf-color-border` to something light breaks
   conformance in their app while our CI stays green. The statement must say: conformance is claimed
   for the **shipped default theme**; a host that re-themes owns re-verifying.
8. **Open:** is a `HeadingLevel` parameter needed? Deferred (resolved decision 7); revisit if a host
   reports a heading-order problem embedding below an `h2`. Additive public API, so any minor can carry it.
9. **Open:** a dark high-contrast variant in C2? Light-on-dark is the more commonly requested mode.
   One extra token block — low cost, real value, some test surface. A scope call.
10. **Open:** Markdown-rendering machinery for E2. `pages.yml` currently only copies files; adding a
    renderer is a small but real new dependency. Either render it (better artifact) or ship
    Markdown-only and link to GitHub (zero machinery, worse impression for the one document whose
    entire point is polish).
