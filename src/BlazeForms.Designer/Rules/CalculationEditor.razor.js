// Collocated ES module for CalculationEditor.razor's focus trap (PRD §4.1, §5, §11, §13).
//
// A deliberate, collocated copy of VisibilityRuleEditor.razor.js's own focus trap -- see that
// module's own remarks for why this is a copy rather than a shared import (AGENTS.md's "JS lives
// in collocated .razor.js ES modules" convention: each dialog owns its own). The genuine platform
// gap is identical here: Tab and Shift+Tab must cycle focus among this dialog's own focusable
// controls without ever letting it escape to the rest of the page while the dialog is open.
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
 * whose `dispose()` detaches it -- called from CalculationEditor.razor.cs's own DisposeAsync,
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
