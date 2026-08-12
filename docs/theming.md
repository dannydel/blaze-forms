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
| `--bf-touch-target` | Minimum interactive target size, 44px (WCAG 2.2 AA, PRD §11). |
| `--bf-motion-duration` | Transition duration; zeroed under `prefers-reduced-motion: reduce`. |
| `--bf-motion-ease` | Transition easing. |
| `--bf-breakpoint-collapse` | The viewport width, 480px, below which half-width field pairs stack to one column (PRD §4.2). |

## Restyling: the component registry

Tokens restyle the shipped components; `IFieldComponentRegistry` (PRD §10) replaces them
outright. A host registers its own design system's component per `NodeType` — the field's
`FormFieldBase` parameter contract (`Fields/FormFieldBase.cs`) is the seam every replacement
subclasses. `samples/` demonstrates the honesty test for this seam with a MudBlazor adapter that
swaps every input component without touching `BlazeForms.Core` or `BlazeForms.Renderer`.

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
