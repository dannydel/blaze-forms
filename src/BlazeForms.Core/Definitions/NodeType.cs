using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace BlazeForms.Definitions;

/// <summary>
/// The kind of a <see cref="FormNode"/>. The JSON name of every member is part of the
/// definition schema contract (PRD §5) and is pinned by the golden-file tests, so members may
/// be added but never renamed without a <c>schemaVersion</c> bump.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<NodeType>))]
public enum NodeType
{
    /// <summary>
    /// A single-line free-text input.
    /// </summary>
    [JsonStringEnumMemberName("text")]
    Text,

    /// <summary>
    /// A multi-line free-text input.
    /// </summary>
    [JsonStringEnumMemberName("textarea")]
    TextArea,

    /// <summary>
    /// A single-line input constrained to an email address.
    /// </summary>
    [JsonStringEnumMemberName("email")]
    Email,

    /// <summary>
    /// A single-line input constrained to a telephone number.
    /// </summary>
    [JsonStringEnumMemberName("phone")]
    Phone,

    /// <summary>
    /// A numeric input, optionally bounded by <see cref="FormNode.Min"/> and
    /// <see cref="FormNode.Max"/>.
    /// </summary>
    [JsonStringEnumMemberName("number")]
    Number,

    /// <summary>
    /// A monetary input, optionally bounded by <see cref="FormNode.Min"/> and
    /// <see cref="FormNode.Max"/>.
    /// </summary>
    [JsonStringEnumMemberName("currency")]
    Currency,

    /// <summary>
    /// A single calendar date.
    /// </summary>
    [JsonStringEnumMemberName("date")]
    Date,

    /// <summary>
    /// A start and end calendar date captured as one answer.
    /// </summary>
    [JsonStringEnumMemberName("daterange")]
    DateRange,

    /// <summary>
    /// A single choice presented as a drop-down list.
    /// </summary>
    [JsonStringEnumMemberName("select")]
    Select,

    /// <summary>
    /// A single choice presented as a radio group.
    /// </summary>
    [JsonStringEnumMemberName("radio")]
    Radio,

    /// <summary>
    /// Zero or more choices presented as a checkbox group.
    /// </summary>
    [JsonStringEnumMemberName("checkboxgroup")]
    CheckboxGroup,

    /// <summary>
    /// A yes-or-no choice presented as a two-option group.
    /// </summary>
    [JsonStringEnumMemberName("yesno")]
    YesNo,

    /// <summary>
    /// A single opt-in checkbox.
    /// </summary>
    [SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "The member name mirrors the 'boolean' node type named in PRD §5; the schema name is the contract.")]
    [JsonStringEnumMemberName("boolean")]
    Boolean,

    /// <summary>
    /// A static section heading, rendered at <see cref="FormNode.Level"/> 2 to 4.
    /// </summary>
    [JsonStringEnumMemberName("heading")]
    Heading,

    /// <summary>
    /// Static prose held in <see cref="FormNode.Content"/>; Markdown-enabled (PRD §5.1).
    /// </summary>
    [JsonStringEnumMemberName("paragraph")]
    Paragraph,

    /// <summary>
    /// A static highlighted notice held in <see cref="FormNode.Content"/>; Markdown-enabled
    /// (PRD §5.1).
    /// </summary>
    [JsonStringEnumMemberName("callout")]
    Callout,

    /// <summary>
    /// A static visual separator.
    /// </summary>
    [JsonStringEnumMemberName("divider")]
    Divider,

    /// <summary>
    /// A read-only computed value. The schema ships in P1; the evaluation engine is P2, so the
    /// renderer shows a read-only placeholder until then (PRD §5).
    /// </summary>
    [JsonStringEnumMemberName("calc")]
    Calc,

    /// <summary>
    /// A repeating group over <see cref="FormNode.Children"/>. Reserved for P2: the schema
    /// represents it, but no editor or renderer ships in P1 (PRD §5).
    /// </summary>
    [JsonStringEnumMemberName("repeating")]
    Repeating,

    /// <summary>
    /// A file upload. Reserved for P2: the schema represents it, but no editor or renderer
    /// ships in P1 (PRD §5).
    /// </summary>
    [JsonStringEnumMemberName("file")]
    File,

    /// <summary>
    /// A reference resolved against an external source. Reserved for P2: the schema represents
    /// it, but no editor or renderer ships in P1 (PRD §5).
    /// </summary>
    [JsonStringEnumMemberName("lookup")]
    Lookup,
}
