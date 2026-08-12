// Collocated ES module for DesignerCanvas.razor's roving-tabindex keyboard model (PRD §11).
//
// The genuine platform gap this module fills: suppressing the browser's own default action for
// ArrowUp/ArrowDown/Home/End (scrolling), Alt+ArrowLeft/Alt+ArrowRight (browser back/forward
// navigation in several browsers), Ctrl+D (the browser's own "bookmark this page" shortcut), and
// Ctrl+Z/Ctrl+Shift+Z (a browser's own document-level undo/redo, where one exists) -- all of
// which DesignerCanvas.razor.cs's own OnKeyDown/HandleAltArrow already handle themselves (moving
// the roving cursor, committing the selection on Enter, reordering, duplicating, or undoing/
// redoing a mutation -- PRD §4.1). Blazor's declarative @onkeydown:preventDefault modifier
// applies unconditionally to every keydown an element receives, including Tab, and there is no
// way to make it conditional per key value -- binding it here would risk trapping Tab the moment
// it bubbled through alongside the keys this module actually cares about. This listener calls
// preventDefault() only for those keys, and only to cancel the browser's own default action -- it
// never stops propagation, so the Blazor @onkeydown handler bound to this exact same element
// still receives, and still handles, every key exactly as before. This module is imported and
// attached only against the canvas's own root element (attachScrollSuppression's caller), so it
// never reaches a text input inside the properties panel elsewhere in the shell -- Ctrl+Z there
// keeps its ordinary text-editing meaning. Delete has no browser default worth suppressing, so it
// is not in either set below.

const SUPPRESSED_KEYS = new Set(["ArrowUp", "ArrowDown", "Home", "End"]);
const ALT_SUPPRESSED_KEYS = new Set(["ArrowLeft", "ArrowRight"]);
const CTRL_SUPPRESSED_KEYS = new Set(["d", "z"]);

function onKeyDown(event) {
    const key = event.key.toLowerCase();

    if (SUPPRESSED_KEYS.has(event.key) || (event.altKey && ALT_SUPPRESSED_KEYS.has(event.key))) {
        event.preventDefault();
    } else if (event.ctrlKey && CTRL_SUPPRESSED_KEYS.has(key)) {
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
