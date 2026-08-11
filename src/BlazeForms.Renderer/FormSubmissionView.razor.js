// Collocated ES module for FormSubmissionView.razor's JSON export (PRD §4.3). File download has
// no pure-Blazor path -- triggering a browser "Save As" requires a real user-gesture-driven
// anchor click -- so this is the one genuine platform gap this component reaches into JS for.
// No globals: everything here is a named export the component imports and calls directly.

export function downloadSubmissionJson(fileName, json) {
    const blob = new Blob([json], { type: "application/json" });
    const url = URL.createObjectURL(blob);

    try {
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = fileName;
        anchor.style.display = "none";
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
    } finally {
        URL.revokeObjectURL(url);
    }
}
