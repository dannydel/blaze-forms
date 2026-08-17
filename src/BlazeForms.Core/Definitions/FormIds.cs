using System.Globalization;

namespace BlazeForms.Definitions;

/// <summary>
/// Generates the machine-generated identifiers the schema requires. Identifiers are opaque:
/// nothing may parse them, and nothing may derive them from author-supplied text (AGENTS.md
/// invariant #5).
/// </summary>
public static class FormIds
{
    /// <summary>
    /// Creates an identifier for a new form.
    /// </summary>
    /// <returns>
    /// A fresh opaque identifier prefixed <c>form-</c>.
    /// </returns>
    public static string NewFormId() => Create("form");

    /// <summary>
    /// Creates an identifier for a new page.
    /// </summary>
    /// <returns>
    /// A fresh opaque identifier prefixed <c>page-</c>.
    /// </returns>
    public static string NewPageId() => Create("page");

    /// <summary>
    /// Creates an identifier for a new section.
    /// </summary>
    /// <returns>
    /// A fresh opaque identifier prefixed <c>section-</c>.
    /// </returns>
    public static string NewSectionId() => Create("section");

    /// <summary>
    /// Creates an identifier for a new node.
    /// </summary>
    /// <returns>
    /// A fresh opaque identifier prefixed <c>node-</c>.
    /// </returns>
    public static string NewNodeId() => Create("node");

    /// <summary>
    /// Creates an identifier for a new submission.
    /// </summary>
    /// <returns>
    /// A fresh opaque identifier prefixed <c>sub-</c>.
    /// </returns>
    public static string NewSubmissionId() => Create("sub");

    /// <summary>
    /// Creates an identifier for a new row of a repeating group (PRD §5). Row identifiers are
    /// opaque and immutable like every other identifier here: they key a row's answers across
    /// add/remove/reorder, draft resume, and the submission envelope, and nothing derives them
    /// from respondent input.
    /// </summary>
    /// <returns>
    /// A fresh opaque identifier prefixed <c>row-</c>.
    /// </returns>
    public static string NewRowId() => Create("row");

    private static string Create(string prefix) =>
        string.Concat(prefix, "-", Guid.NewGuid().ToString("n", CultureInfo.InvariantCulture));
}
