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
