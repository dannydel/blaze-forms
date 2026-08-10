namespace BlazeForms.Markdown;

/// <summary>
/// HTML produced by the safe-Markdown pipeline (PRD §5.1). The type is the invariant: a value of
/// this type has already passed through <see cref="SafeMarkdown.ToHtml"/> — raw HTML disabled,
/// link protocols allow-listed, images stripped — so a caller that accepts a
/// <see cref="SafeHtml"/> rather than a plain <see cref="string"/> cannot be handed unsanitized
/// markup by mistake (AGENTS.md invariant #6).
/// </summary>
public readonly record struct SafeHtml
{
    private readonly string? _value;

    /// <summary>
    /// Wraps an already-sanitized HTML string. Callers outside the pipeline should not construct
    /// this directly; obtain one from <see cref="SafeMarkdown.ToHtml"/>.
    /// </summary>
    /// <param name="value">
    /// The sanitized HTML to carry.
    /// </param>
    public SafeHtml(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        _value = value;
    }

    /// <summary>
    /// The sanitized HTML. Reads as the empty string for a default-constructed value.
    /// </summary>
    public string Value => _value ?? "";

    /// <summary>
    /// Returns the sanitized HTML, so the value interpolates and logs as its markup.
    /// </summary>
    /// <returns>
    /// The value of <see cref="Value"/>.
    /// </returns>
    public override string ToString() => Value;
}
