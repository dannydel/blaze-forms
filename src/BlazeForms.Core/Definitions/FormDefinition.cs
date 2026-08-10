using System.Text.Json.Serialization;
using BlazeForms.Expressions;

namespace BlazeForms.Definitions;

/// <summary>
/// A whole form, as data: <c>FormDefinition → Page[] → Section[] → Node[]</c> (PRD §5). The
/// serialized shape is a public contract, so any change to it bumps
/// <see cref="SchemaVersion"/> and lands with updated round-trip and golden-file tests
/// (AGENTS.md invariant #2).
/// </summary>
/// <remarks>
/// <para>
/// Definitions are untrusted input, so every collection property here reads as empty rather than
/// null when a document omits it. Records with <c>required</c> members cannot be created through
/// their default constructor, which means property initializers never run during
/// deserialization — the guard lives in the accessor for that reason.
/// </para>
/// <para>
/// Every collection accessor also takes a read-only defensive copy. Without one, a caller that
/// kept hold of the <see cref="List{T}"/> it passed in could mutate a published version through
/// that reference, which AGENTS.md invariant #3 forbids outright.
/// </para>
/// </remarks>
public sealed record FormDefinition
{
    private readonly int _schemaVersion;
    private readonly IReadOnlyList<FormPage>? _pages;
    private readonly IReadOnlyList<ValidationRule>? _validationRules;

    /// <summary>
    /// The version of the definition schema this document was written against. A document that
    /// omits it is read as <see cref="FormSchema.CurrentVersion"/>, since only a v1 document could
    /// predate the property; a document that <em>states</em> a version this build does not know is
    /// rejected outright by <see cref="Serialization.FormJson.DeserializeDefinition"/>.
    /// </summary>
    /// <remarks>
    /// Zero reads as the current version because it is indistinguishable from absent here —
    /// System.Text.Json assigns absent properties their default. Rejecting an explicitly declared
    /// zero is therefore the deserializer's job, not this accessor's.
    /// </remarks>
    [JsonPropertyOrder(-1)]
    public int SchemaVersion
    {
        get => _schemaVersion == 0 ? FormSchema.CurrentVersion : _schemaVersion;
        init => _schemaVersion = value;
    }

    /// <summary>
    /// The machine-generated, immutable identifier of the form this definition belongs to.
    /// Stable across every version of the form.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The form's display name. Plain text always.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// A short description of the form's purpose. Plain text always.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The owning program, used by the library surface to group and filter forms (PRD §4.4).
    /// </summary>
    public string? Program { get; init; }

    /// <summary>
    /// An opaque host-supplied owner key for the form, used by the library surface to filter
    /// (PRD §4.4).
    /// </summary>
    public string? Owner { get; init; }

    /// <summary>
    /// The form's pages, in the order the respondent meets them.
    /// </summary>
    public IReadOnlyList<FormPage> Pages
    {
        get => _pages ?? [];
        init => _pages = value is null ? null : Array.AsReadOnly<FormPage>([.. value]);
    }

    /// <summary>
    /// Cross-field validation rules evaluated against the whole form (PRD §6).
    /// </summary>
    public IReadOnlyList<ValidationRule> ValidationRules
    {
        get => _validationRules ?? [];
        init => _validationRules = value is null ? null : Array.AsReadOnly<ValidationRule>([.. value]);
    }
}
