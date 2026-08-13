// Collocated ES module for DesignerCanvas.razor's roving-tabindex keyboard model (PRD §11) and
// the drag-and-drop drop indicator (PRD §4.1).
//
// Keyboard: the genuine platform gap this module fills is suppressing the browser's own default
// action for ArrowUp/ArrowDown/Home/End (scrolling), Alt+ArrowLeft/Alt+ArrowRight (browser
// back/forward navigation in several browsers), Ctrl+D (the browser's own "bookmark this page"
// shortcut), and Ctrl+Z/Ctrl+Shift+Z (a browser's own document-level undo/redo, where one exists)
// -- all of which DesignerCanvas.razor.cs's own OnKeyDown/HandleAltArrow already handle themselves
// (moving the roving cursor, committing the selection on Enter, reordering, duplicating, or
// undoing/redoing a mutation -- PRD §4.1). Blazor's declarative @onkeydown:preventDefault modifier
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
//
// Drop indicator: reordering itself is decided entirely by DesignerCanvas.razor.cs's DropOnRow
// (inserts the dragged node immediately before the target row) and DropOnSection (appends to the
// end of a section, the fallback for the empty space below the last row). Rendering a live line
// as the pointer moves is a genuine platform gap -- with the host on InteractiveServer, a C#
// @ondragover/@ondragenter would round-trip every pointer move over SignalR. This module instead
// tracks the hovered drop target itself and toggles a CSS class that draws the line, purely
// visual, mirroring the exact spot the C# drop handler would place the node.

const SUPPRESSED_KEYS = new Set(["ArrowUp", "ArrowDown", "Home", "End"]);
const ALT_SUPPRESSED_KEYS = new Set(["ArrowLeft", "ArrowRight"]);
const CTRL_SUPPRESSED_KEYS = new Set(["d", "z"]);

const ROW_SELECTOR = ".bf-canvas-row";
const SECTION_ROWS_SELECTOR = ".bf-canvas-section__rows";
const ROW_DROP_CLASS = "bf-canvas-row--drop-before";
const SECTION_DROP_CLASS = "bf-canvas-section__rows--drop-end";

function onKeyDown(event) {
    const key = event.key.toLowerCase();

    if (SUPPRESSED_KEYS.has(event.key) || (event.altKey && ALT_SUPPRESSED_KEYS.has(event.key))) {
        event.preventDefault();
    } else if (event.ctrlKey && CTRL_SUPPRESSED_KEYS.has(key)) {
        event.preventDefault();
    }
}

/**
 * Attaches the scroll-suppressing keydown listener and the drag-and-drop drop-indicator
 * listeners to the canvas's root element. Returns a handle whose `dispose()` detaches all of
 * them -- called from DesignerCanvas.razor.cs's own DisposeAsync, mirroring the disposable-JS-
 * object-reference convention every other collocated module in this codebase follows.
 */
export function attachScrollSuppression(canvasElement) {
    // Per-attach state so multiple canvases (or repeated attach/dispose cycles) never share or
    // collide over which row/section is currently marked.
    let dragActive = false;
    let markedRow = null;
    let markedSection = null;
    let draggedRow = null;

    function clearIndicators() {
        if (markedRow !== null) {
            markedRow.classList.remove(ROW_DROP_CLASS);
            markedRow = null;
        }
        if (markedSection !== null) {
            markedSection.classList.remove(SECTION_DROP_CLASS);
            markedSection = null;
        }
    }

    function onDragStart(event) {
        // Only an internal row drag arms the indicator; a stray external drag (a file, selected
        // text) is a silent no-op in C#, so it gets no indicator either.
        if (event.target.closest(ROW_SELECTOR)) {
            dragActive = true;
            draggedRow = event.target.closest(ROW_SELECTOR);
        }
    }

    function onDragOver(event) {
        if (!dragActive) {
            return;
        }

        const row = event.target.closest(ROW_SELECTOR);
        if (row && row === draggedRow) {
            // Hovering the row being dragged itself is not a move -- DropOnRow would insert it
            // right back where it already is, so show no line rather than imply one that won't
            // happen.
            clearIndicators();
            return;
        }

        if (row) {
            if (row !== markedRow) {
                clearIndicators();
                row.classList.add(ROW_DROP_CLASS);
                markedRow = row;
            }
            return;
        }

        const sectionRows = event.target.closest(SECTION_ROWS_SELECTOR);
        if (sectionRows) {
            if (sectionRows !== markedSection) {
                clearIndicators();
                sectionRows.classList.add(SECTION_DROP_CLASS);
                markedSection = sectionRows;
            }
            return;
        }

        clearIndicators();
    }

    function onDragLeave(event) {
        // Clears only when the pointer leaves the canvas entirely, not when it merely crosses
        // from one child element to another within it.
        if (!canvasElement.contains(event.relatedTarget)) {
            clearIndicators();
        }
    }

    function onDrop() {
        dragActive = false;
        draggedRow = null;
        clearIndicators();
    }

    function onDragEnd() {
        // A cancelled drag fires dragend but not drop, so both handlers reset state identically.
        dragActive = false;
        draggedRow = null;
        clearIndicators();
    }

    canvasElement.addEventListener("keydown", onKeyDown);
    canvasElement.addEventListener("dragstart", onDragStart);
    canvasElement.addEventListener("dragover", onDragOver);
    canvasElement.addEventListener("dragleave", onDragLeave);
    canvasElement.addEventListener("drop", onDrop);
    canvasElement.addEventListener("dragend", onDragEnd);

    return {
        dispose() {
            canvasElement.removeEventListener("keydown", onKeyDown);
            canvasElement.removeEventListener("dragstart", onDragStart);
            canvasElement.removeEventListener("dragover", onDragOver);
            canvasElement.removeEventListener("dragleave", onDragLeave);
            canvasElement.removeEventListener("drop", onDrop);
            canvasElement.removeEventListener("dragend", onDragEnd);
        },
    };
}
