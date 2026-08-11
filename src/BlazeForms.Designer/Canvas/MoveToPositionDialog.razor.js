// Collocated ES module for MoveToPositionDialog.razor's focus trap (PRD §4.1, §11).
//
// The genuine platform gap this module fills: Tab and Shift+Tab must cycle focus among this
// dialog's own focusable controls without ever letting it escape to the rest of the page while
// the dialog is open. Blazor's declarative @onkeydown:preventDefault modifier cannot be made
// conditional per key value -- there is no way to say "prevent the default only for Tab, and only
// when Tab would actually leave the dialog, but never touch any other key" -- the same platform
// gap DesignerCanvas.razor.js documents for its own scroll suppression. This listener calls
// preventDefault() only in the two cases it is actually about to redirect focus itself, and never
// stops propagation, so Escape (handled entirely on the Blazor side) and every other key still
// reach the dialog's own @onkeydown handler untouched.
//
// No globals: every export is a named function the component imports and calls directly.

function focusableElements(root) {
    return Array.from(root.querySelectorAll("select, button, input, [tabindex]"))
        .filter((element) => !element.disabled && element.tabIndex !== -1);
}

function onKeyDown(event, root) {
    if (event.key !== "Tab") {
        return;
    }

    const elements = focusableElements(root);

    if (elements.length === 0) {
        return;
    }

    const first = elements[0];
    const last = elements[elements.length - 1];

    if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
    }
}

/**
 * Attaches the Tab-cycling focus-trap listener to the dialog's root element. Returns a handle
 * whose `dispose()` detaches it -- called from MoveToPositionDialog.razor.cs's own DisposeAsync,
 * mirroring the disposable-JS-object-reference convention every other collocated module in this
 * codebase follows.
 */
export function attachFocusTrap(root) {
    const handler = (event) => onKeyDown(event, root);
    root.addEventListener("keydown", handler);

    return {
        dispose() {
            root.removeEventListener("keydown", handler);
        },
    };
}
