# Theming

BlazeForms.Renderer ships a neutral default theme and a small, documented set of `--bf-*` CSS
custom properties. The token set — not any particular stylesheet — is the public theming
contract (PRD §10).

## Using the shipped theme

Add one `<link>` to the host page:

```html
<link rel="stylesheet" href="_content/BlazeForms.Renderer/blazeforms.css" />
```

Component-scoped CSS (the `.razor.css` files collocated with each field/structure component) is
bundled automatically by the Blazor build into `BlazeForms.Renderer.styles.css` — reference that
too, the way any Razor Class Library's isolated CSS is referenced. Nothing else is required to
render a legible, accessible form.

## Restyling: the token contract

Every color, typographic, spacing, radius, border, focus, and motion decision made by a shipped
component resolves through one of the tokens below. To restyle the renderer, re-declare these
properties — on `:root`, or on any ancestor of the rendered form to scope the override — and
change nothing else. No build step and no Tailwind toolchain is required downstream; Tailwind is
used only to *produce* the shipped default theme at library build time (PRD §10), and that
pipeline is deferred to a later slice — today's `blazeforms.css` is hand-authored, plain CSS.

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
