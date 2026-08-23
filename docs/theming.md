# Theming

BlazeForms.Renderer ships a neutral default theme and a small, documented set of `--bf-*` CSS
custom properties. The token set — not any particular stylesheet — is the public theming
contract (PRD §10).

## Using the shipped theme

Add one `<link>` to the host page:

```html
<link rel="stylesheet" href="_content/BlazeForms.Renderer/blazeforms.css" />
```

**This stylesheet is functionally required, not an optional default look a host can skip in
favor of mapping the `--bf-*` tokens somewhere else.** It carries structural CSS with no token
equivalent: the `--bf-breakpoint-dock-collapse`/`--bf-breakpoint-collapse` media queries, the
`prefers-reduced-motion` zeroing, and — load-bearing for accessibility — the global
`.bf-visually-hidden` rule `FormRenderer`'s own aria-live announcer regions render with (nothing
in `BlazeForms.Designer` uses this class today). A host that never links `blazeforms.css` at all
renders those announcer regions as visible page text instead of assistive-technology-only
content. A host restyling the renderer still links this stylesheet and *re-declares* tokens on
top of it (see "Restyling" below) — it never replaces it outright.

Component-scoped CSS (the `.razor.css` files collocated with each field/structure component) is
bundled automatically by the Blazor build into `BlazeForms.Renderer.styles.css` — reference that
too, the way any Razor Class Library's isolated CSS is referenced.

## Restyling: the token contract

Every color, typographic, spacing, radius, border, focus, and motion decision made by a shipped
component resolves through one of the tokens below. To restyle the renderer, re-declare these
properties — on `:root`, or on any ancestor of the rendered form to scope the override — and
change nothing else. No build step and no Tailwind toolchain is required downstream; Tailwind is
used only to *produce* the shipped default theme at library build time (PRD §10), and that
pipeline is deferred to a later slice — today's `blazeforms.css` is hand-authored, plain CSS.

**Cascade order matters.** A `--bf-*` re-declaration and `blazeforms.css`'s own `:root` block are
declared at the same selector specificity, so whichever `<link>` loads LAST wins a given token.
Load your own stylesheet AFTER `blazeforms.css`, or a re-declared token is silently overridden by
the RCL's own default instead of taking effect — contradicting "re-declare and change nothing
else" above.

| Token | Purpose |
|---|---|
| `--bf-color-bg` | Page/field background. |
| `--bf-color-surface` | Recessed surfaces (disabled fields, callouts). |
| `--bf-color-text` | Primary text color. |
| `--bf-color-muted` | Secondary text (help text, disabled text). |
| `--bf-color-border` | Default border color for inputs, dividers, fieldsets. |
| `--bf-color-primary` | Interactive accent (links, focus accents, checked controls). |
| `--bf-color-primary-contrast` | Text/icon color on top of `--bf-color-primary`. |
| `--bf-color-danger` | Error state (invalid borders, error text). |
| `--bf-color-danger-contrast` | Text/icon color on top of `--bf-color-danger`. |
| `--bf-color-focus-ring` | The visible focus ring drawn on every interactive element. |
| `--bf-font-sans` | The font stack for all chrome and field text. |
| `--bf-font-size-sm` | Small text (help, error, secondary labels). |
| `--bf-font-size-base` | Body/control text. |
| `--bf-font-size-lg` | Headings and emphasis. |
| `--bf-line-height` | Body line height. |
| `--bf-space-1` … `--bf-space-6` | The spacing scale, smallest to largest, used for every margin/padding/gap. |
| `--bf-radius-sm` | Corner radius for inputs and small controls. |
| `--bf-radius-md` | Corner radius for larger surfaces (callouts, cards). |
| `--bf-border-width` | Default border thickness. |
| `--bf-focus-ring-width` | Focus ring thickness. |
| `--bf-focus-ring-offset` | Focus ring offset from the element it outlines. |
| `--bf-touch-target` | Minimum interactive *hit-area* block-size, 44px — deliberately exceeding the WCAG 2.2 AA floor (2.5.8 Target Size (Minimum) is 24×24 CSS px at AA; 44px is 2.5.5 Target Size (Enhanced), AAA) (PRD §11). Sets `min-height` directly on each text input/textarea/select control itself, and on the `.bf-choice` wrapper around a radio/checkbox (the only one of these that *is* a wrapper) — the radio/checkbox glyph itself is sized by `--bf-space-5` (24px), still comfortably at the AA floor on its own. |
| `--bf-motion-duration` | Transition duration; zeroed under `prefers-reduced-motion: reduce`. |
| `--bf-motion-ease` | Transition easing. |
| `--bf-breakpoint-collapse` | The viewport width, 480px, below which half-width field pairs stack to one column (PRD §4.2). **Documentation only**: no stylesheet can consume this via `var()` — a `@media` condition can't reference a custom property — so re-declaring it has zero effect; `blazeforms.css`'s own `@media (max-width: 480px)` literal is what actually governs the breakpoint (kept in sync by `ThemeCssTests.CollapseBreakpointMediaQueryLiteralMatchesItsOwnToken`). |

### Contrast guarantees

The shipped default theme's color tokens are computed (`tests/BlazeForms.Renderer.Tests/ThemeContrastTests.cs`)
against the WCAG 2.x relative-luminance formula, not eyeballed — a re-theming host inherits the
same floor its own re-declared values are on the hook for:

| Token pair | Minimum ratio | Criterion |
|---|---|---|
| `--bf-color-text`, `--bf-color-muted`, `--bf-color-primary`, `--bf-color-danger` each against `--bf-color-bg` and `--bf-color-surface` | 4.5:1 | 1.4.3 Contrast (Minimum) |
| `--bf-color-primary-contrast` against `--bf-color-primary`; `--bf-color-danger-contrast` against `--bf-color-danger` | 4.5:1 | 1.4.3 Contrast (Minimum) |
| `--bf-color-border`, `--bf-color-focus-ring` each against `--bf-color-bg` and `--bf-color-surface` | 3:1 | 1.4.11 Non-text Contrast |

A host that re-declares any of these tokens — including on an ancestor that only scopes part of
the page — re-verifies its own replacement against the same floor; conformance is claimed for the
shipped default values only.

## Restyling: the component registry

Tokens restyle the shipped components; `IFieldComponentRegistry` (PRD §10) replaces them
outright. A host registers its own design system's component per `NodeType` — the field's
`FormFieldBase` parameter contract (`Fields/FormFieldBase.cs`) is the seam every replacement
subclasses. `samples/` demonstrates the honesty test for this seam with a MudBlazor adapter that
swaps every input component without touching `BlazeForms.Core` or `BlazeForms.Renderer`.

## Designer tokens

`BlazeForms.Designer` adds a small set of its own `--bf-*` tokens for the three-pane docked
layout (PRD §4.1, D9 — docked is the only P1 layout). They are new, not a restatement of the
renderer's — `FormDesigner`'s own component CSS otherwise builds directly on the color,
typography, spacing, radius, and focus tokens `blazeforms.css` already declares, since
`BlazeForms.Designer` depends on `BlazeForms.Renderer` (PRD §9). A host that links `FormDesigner`
links both stylesheets:

```html
<link rel="stylesheet" href="_content/BlazeForms.Renderer/blazeforms.css" />
<link rel="stylesheet" href="_content/BlazeForms.Designer/blazeforms-designer.css" />
```

| Token | Purpose |
|---|---|
| `--bf-palette-width` | The field palette pane's fixed column width in the docked layout. |
| `--bf-properties-width` | The properties pane's fixed column width in the docked layout. |
| `--bf-pane-gap` | The gap between the three docked panes. |
| `--bf-dock-height` | The docked shell's minimum height; panes scroll independently past it. |
| `--bf-canvas-row-selected-bg` | `DesignerCanvas`'s selected node row background — a designer-only concept the renderer's own token set has no equivalent for. |
| `--bf-publish-note-min-height` | `PublishDialog`'s change-note textarea minimum height. |
| `--bf-breakpoint-dock-collapse` | The viewport width, 60rem (960px), below which the three-pane docked layout stacks to a single column in DOM order — palette, canvas, properties (WCAG 1.4.10 Reflow). **Documentation only**, for the same reason `--bf-breakpoint-collapse` above is: `FormDesigner.razor.css`'s own `@media (max-width: 60rem)` literal is what actually governs the breakpoint (kept in sync by `DesignerThemeCssTests.DockCollapseBreakpointMediaQueryLiteralMatchesItsOwnToken`). Note the unit mismatch with `--bf-breakpoint-collapse` (`px` there, `rem` here) — nothing forces the two breakpoint token families to agree on a unit; treat each literally. |

## High contrast

BlazeForms.Renderer ships a high-contrast story in two parts that solve different problems
(docs/accessibility-statement-plan.md resolved decision 1) — one is correctness hardening that
applies automatically, the other is an opt-in theme a host chooses to offer. Every claim below
traces to a named test in `ThemeContrastTests`, `DesignerContrastTests`, or
`HighContrastAccessibilityTests`, each of which was verified to actually fail when the mechanism
it proves is reverted — see those files' own remarks for the reverted-state failure each guards.

**`forced-colors: active` hardening (automatic, no opt-in).** Windows High Contrast mode and
equivalent OS/browser forced-color-schemes replace every author-specified color with a small,
fixed system palette — this is not something a host or BlazeForms can turn off, and the default
theme's own colors are simply not painted while it is active. Two places in the shipped CSS harden
against that erasing an affordance that would otherwise rely on color alone:

- Every focus ring maps to the `Highlight` system color (`--bf-color-focus-ring` is re-declared
  under `@media (forced-colors: active)` in `blazeforms.css`, so every component's own
  `:focus-visible` rule inherits it with no `.razor.css` change). This rule is placed AFTER both
  the opt-in theme block and the `prefers-contrast: more` fold below, and matches
  `:root, [data-bf-theme="high-contrast"]` rather than `:root` alone — both are load-bearing: an
  element that carries the opt-in theme's own attribute shares this rule's exact specificity, so
  without the later position and the extra selector, opting into the theme while forced colors is
  also active would silently lose this hardening back to the theme's own accent color
  (`HighContrastAccessibilityTests.TheOptInThemeDoesNotDefeatForcedColorsFocusRingHardening`).
- An invalid `.bf-field` input/textarea/select's border maps to `Mark`, distinct from the plain
  `CanvasText` forced colors gives every other border, reinforcing (not replacing) the text error
  message every invalid field already renders. **Scope, precisely:** this reaches inputs matched
  by `.bf-field input[aria-invalid="true"]` etc. only — `RadioGroupField`, `CheckboxGroupField`,
  `YesNoField`, and `DateRangeField`'s own boundary are `.bf-fieldset`-rooted and never match this
  selector at all, in either theme; their invalid state is conveyed by the same text message and
  `aria-invalid` alone, in every theme, not by a border change. This rule is placed AFTER the
  unconditional `border-color: var(--bf-color-danger)` rule those same elements otherwise get
  (same selector, so identical specificity) — a media query adds no specificity of its own, only
  source order decides a tie, and this is exactly the bug the CSS's own remarks document by name.
- `BlazeForms.Designer`'s canvas gives the selected row a structural, non-color cue (a widened
  `border-inline-start`, plus the `SelectedItem`/`SelectedItemText` system colors) under the same
  media query — `--bf-canvas-row-selected-bg` is a `color-mix()` **background**, and forced colors
  collapses every author background to the same flat `Canvas`, so without this the selected row
  would be visually indistinguishable from an unselected one for exactly the users forced colors
  exists for.

Nothing above is a token a host restyles; it is unconditional hardening, verified by
`tests/BlazeForms.E2E.Tests/HighContrastAccessibilityTests.cs` emulating `forced-colors: active`
with a positive precondition (`window.matchMedia('(forced-colors: active)').matches`) before every
scan — a scan that ran without forced colors actually active would still report zero violations,
since the default theme is itself axe-clean, so a scan alone proves nothing without that check.

**The opt-in `[data-bf-theme="high-contrast"]` theme.** Set the attribute on **any element** — not
only `<html>` — to switch every color token, for that element and its descendants, to a palette
where every RENDERER text and non-text pair clears **7:1** — well above the 4.5:1/3:1 AA floor the
shipped default theme itself only has to clear:

```html
<html data-bf-theme="high-contrast">
```

or scoped to part of a page, via ordinary custom-property inheritance:

```html
<div data-bf-theme="high-contrast">
  <!-- a rendered form here re-themes; the rest of the page does not -->
</div>
```

The selector is deliberately the bare attribute, `[data-bf-theme="high-contrast"]`, never
`:root[data-bf-theme="high-contrast"]` — `:root` matches only the document root, which would
silently no-op the scoped form above.

This is a color-only re-declaration of the same token contract documented above — no separate
stylesheet, no `.razor.css` edit anywhere in either RCL. That constraint doubles as the honesty
test for the token contract this document advertises: if a theme this different could only be
expressed by editing component CSS, the "restyle by re-declaring tokens" claim above would be
false. `--bf-focus-ring-width` is also widened to `3px` in this theme — the one non-color value in
the block, included because a thicker ring is part of the same "more legible" intent and costs
nothing extra once it is already a token.

A user with the OS-level `prefers-contrast: more` preference set gets the identical palette
automatically, via a `@media (prefers-contrast: more) { :root { ... } }` block carrying the same
literal values — folded into the one palette rather than becoming a third, independently
maintained one (resolved decision 1), and `ThemeContrastTests` asserts the two blocks stay in sync
token-for-token. **Precondition, not unconditional:** that fold is a plain `:root`-specificity
rule, like every other token re-declaration in this file — a host stylesheet that re-declares
`--bf-color-*` on `:root` and loads AFTER `blazeforms.css` (the "Restyling" section above's own
documented cascade order) silently cancels the fold for that host, the same as it would cancel any
other default. This is intentional, not an oversight: raising this one media feature's specificity
above every other token in the contract would be a worse inconsistency than the fold being
overridable.

### Designer-specific token-contract gap (disclosed)

The Designer's own `--bf-canvas-row-selected-bg` (`blazeforms-designer.css`) derives from the
RENDERER's `--bf-color-primary`/`--bf-color-bg` via `color-mix()`. Under the high-contrast theme's
pure black/white/navy palette, that derived color measures well under even the shipped default
theme's own 3:1 floor for this pairing — `--bf-color-primary` and `--bf-color-bg` alone don't span
a luminance range that clears it at any mix percentage. `blazeforms-designer.css` therefore carries
its own `[data-bf-theme="high-contrast"]` override with a value picked directly (not derived) to
clear the boundary (≥3:1 against the theme's pure-black border) and the row's own label text
(`--bf-color-text` on it, ≥4.5:1) — **but not** `--bf-color-muted` rendered on that same background
(a row's own visibility-summary/chip text), which measures ~3.46:1 there, below the 4.5:1 floor its
primary-text sibling clears on the identical background. This is a real, measured limitation, not
a rounding error, and `DesignerContrastTests` pins the exact ratio so a future change to either
value is caught rather than silently drifting further. The takeaway for a host: the renderer's own
stylesheet has no way to see or fix a second package's derived token, so a two-package high-
contrast theme needs (and got) an override in each package's own stylesheet.

### Known limitations

- **Light background only.** There is no `high-contrast-dark` (light-on-dark) variant. A host
  wanting a dark high-contrast theme must author its own token block; this document does not claim
  one is provided.
- **A host with a dark brand theme is force-flipped to white, unconditionally, by
  `prefers-contrast: more`.** Because that media-query fold re-declares the SAME light-background
  literals as the opt-in theme (by design — see above), a respondent with that OS preference set
  gets a white-background form even if the host's own default theme is dark, with no documented
  opt-out other than the host re-declaring its own colors on `:root` after `blazeforms.css` (which
  cancels the fold for that host entirely, per the precondition above).
- **Non-`.bf-field` invalid boundaries.** `RadioGroupField`/`CheckboxGroupField`/`YesNoField`/
  `DateRangeField` never get a border-based invalid cue in ANY theme (default, opt-in high
  contrast, or forced colors) — pre-existing, not introduced by this theme, and not fixed here;
  their `aria-invalid` + text message is the only signal today.

### The demo toggle

The WASM demo (`samples/BlazeForms.Demo.Wasm`) ships a real toggle button for the opt-in theme in
its own layout (`MainLayout`), so the claim above is something a reviewer can click rather than
only read about. `aria-pressed` is its only state signal — the button's own label never changes
text, so assistive technology announces exactly the pressed/not-pressed state rather than that plus
a redundant "On"/"Off" suffix. **The toggle does not persist across a reload** — it is plain
in-memory component state, consistent with the rest of the demo's own "everything here lives only
in this tab's memory" banner; a reviewer who reloads the page returns to the default theme.

## Headings and document structure

Neither RCL ever emits an `<h1>`: the document heading is the host page's own responsibility, not
a library's (a form embedded halfway down a page cannot know whether it owns the page's only
`h1`, or one of several). **The shallowest heading either RCL emits is `h2`** — the current
page's title, a dialog's title, `FormCard`'s own name, and so on. Author-configurable heading
blocks (`HeadingBlock`, the definition's own `Heading` node type) and internal sub-structure
(`FormSubmissionView`'s per-page/per-section headings, the Designer's `PreviewPane`,
`CanvasSection`, and `FieldPalette`) go to `h3`/`h4` beneath that. A `HeadingLevel` parameter that
would let a host shift this whole structure down further (for embedding under an `h3`, say) is
deferred until one actually needs it — additive, so any minor release can add it later without
breaking anything.

A host using `<FocusOnNavigate Selector="h1">` (the standard Blazor Router pattern, and the one
`samples/BlazeForms.Sample/Components/Routes.razor` and
`samples/BlazeForms.Demo.Wasm/Components/Routes.razor` both use) therefore needs a real `<h1>` on
every routable page, including one that renders nothing but `FormDesigner`'s own three-pane shell
— which supplies no heading of its own to be that target. `.bf-visually-hidden` (declared once,
globally, in `blazeforms.css` — never CSS-isolated, so it works on markup either host page
renders, not only markup `FormRenderer` itself renders) is the documented way to satisfy that
without a redundant visible caption above the shell:

```razor
<h1 class="bf-visually-hidden">Design</h1>
<FormDesigner ... />
```

Both sample hosts' own `Design.razor` do exactly this. Skipping it is a focus-management defect —
no WCAG success criterion mandates that focus move on every SPA navigation, but 2.4.3 Focus Order
is the closest one, since a host that opts into `FocusOnNavigate` and then gives it nothing to
land on gets silent, unpredictable focus behavior on navigation instead of the deterministic
order that pattern exists to guarantee.

## A worked example: mapping Bootstrap tokens

Bootstrap's own custom properties map onto `--bf-*` directly, proving the CSS-only restyling path
without a component registry:

```css
:root {
  --bf-color-bg: var(--bs-body-bg);
  --bf-color-text: var(--bs-body-color);
  --bf-color-border: var(--bs-border-color);
  --bf-color-primary: var(--bs-primary);
  --bf-color-primary-contrast: #fff;
  --bf-color-danger: var(--bs-danger);
  --bf-color-danger-contrast: #fff;
  --bf-color-focus-ring: var(--bs-primary);
  --bf-font-sans: var(--bs-body-font-family);
  --bf-radius-sm: var(--bs-border-radius-sm);
  --bf-radius-md: var(--bs-border-radius);
}
```
