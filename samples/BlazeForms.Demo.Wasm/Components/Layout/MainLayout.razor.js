// Collocated ES module for MainLayout.razor's high-contrast theme toggle
// (docs/accessibility-statement-plan.md, Increment C4).
//
// The genuine platform gap this module fills: `[data-bf-theme="high-contrast"]` (blazeforms.css)
// is read off `<html>`, an element outside Blazor's own component tree -- there is no Blazor
// markup on the page that owns `<html>` itself for a declarative attribute binding to target.
// Everything else about this toggle (its aria-pressed state, its visible label, the click
// handler) is owned by MainLayout.razor/.razor.cs, imported once via
// IJSRuntime.InvokeAsync<IJSObjectReference>("import", ...) the same way every other collocated
// module in this codebase is -- this module does nothing but the one DOM mutation Blazor cannot
// reach itself. No globals, no eval.

/**
 * Applies or removes data-bf-theme="high-contrast" on the document root, called from
 * MainLayout.razor.cs's own ToggleHighContrastAsync after it flips its _isHighContrast field.
 */
export function setHighContrastTheme(enabled) {
    if (enabled) {
        document.documentElement.setAttribute("data-bf-theme", "high-contrast");
    } else {
        document.documentElement.removeAttribute("data-bf-theme");
    }
}
