// Collocated ES module for RepeatingGroup.razor's Add-row focus behavior (AGENTS.md invariant
// #4). Blazor's own ElementReference API only reaches an element this component itself renders
// with @ref -- a newly added row's own first focusable control is rendered by an arbitrary,
// host-resolvable field component (DynamicComponent), so there is no ElementReference to capture
// for it. Focusing "the first focusable control of the new row" therefore requires querying the
// DOM by the row's own container id, a genuine platform gap Blazor gives no pure-C# path around.
// No globals: this is the one named export the component imports and calls directly.

export function focusFirstControlIn(containerId) {
    const container = document.getElementById(containerId);
    const focusable = container?.querySelector("input, select, textarea, button, [tabindex]");
    focusable?.focus();
}
