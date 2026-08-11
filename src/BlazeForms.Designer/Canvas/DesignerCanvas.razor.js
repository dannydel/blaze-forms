// Collocated ES module for DesignerCanvas.razor's roving-tabindex keyboard model (PRD §11).
//
// The genuine platform gap this module fills: suppressing the browser's own default scroll
// action for ArrowUp/ArrowDown/Home/End, which DesignerCanvas.razor.cs's OnKeyDown already
// handles itself (moving the roving cursor, or committing the selection on Enter). Blazor's
// declarative @onkeydown:preventDefault modifier applies unconditionally to every keydown an
// element receives, including Tab, and there is no way to make it conditional per key value --
// binding it here would risk trapping Tab the moment it bubbled through alongside the four keys
// this module actually cares about. This listener calls preventDefault() only for those four
// keys, and only to cancel the browser's default action -- it never stops propagation, so the
// Blazor @onkeydown handler bound to this exact same element still receives, and still handles,
// every key exactly as before.
//
// No globals: every export is a named function the component imports and calls directly.

const SUPPRESSED_KEYS = new Set(["ArrowUp", "ArrowDown", "Home", "End"]);

function onKeyDown(event) {
    if (SUPPRESSED_KEYS.has(event.key)) {
        event.preventDefault();
    }
}

/**
 * Attaches the scroll-suppressing keydown listener to the canvas's root element. Returns a
 * handle whose `dispose()` detaches it -- called from DesignerCanvas.razor.cs's own
 * DisposeAsync, mirroring the disposable-JS-object-reference convention every other collocated
 * module in this codebase follows.
 */
export function attachScrollSuppression(canvasElement) {
    canvasElement.addEventListener("keydown", onKeyDown);

    return {
        dispose() {
            canvasElement.removeEventListener("keydown", onKeyDown);
        },
    };
}
