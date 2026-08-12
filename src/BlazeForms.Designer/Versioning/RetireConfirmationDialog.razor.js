// Collocated ES module for RetireConfirmationDialog.razor's focus trap (PRD §7, §11).
//
// The genuine platform gap this module fills: Tab and Shift+Tab must cycle focus between this
// dialog's own two buttons without ever letting it escape to the rest of the page while the
// dialog is open -- the exact same gap DeleteProtectionDialog.razor.js documents for its own
// trap, which this module mirrors. This listener calls preventDefault() only in the two cases it
// is actually about to redirect focus itself, and never stops propagation, so Escape (handled
// entirely on the Blazor side) and every other key still reach the dialog's own @onkeydown
// handler untouched.
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
 * whose `dispose()` detaches it -- called from RetireConfirmationDialog.razor.cs's own
 * DisposeAsync, mirroring the disposable-JS-object-reference convention every other collocated
 * module in this codebase follows.
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
