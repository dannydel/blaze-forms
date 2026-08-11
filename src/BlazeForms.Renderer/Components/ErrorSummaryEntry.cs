namespace BlazeForms.Components;

/// <summary>
/// One entry in <see cref="ErrorSummary"/>: a remedy-worded message and the namespaced DOM id of
/// the field it anchors to. <see cref="FormRenderer"/> builds these in document order from the
/// same node walk validation ran over, so the summary and the fields agree on both content and
/// order (PRD §4.2, §11).
/// </summary>
/// <param name="FieldDomId">
/// The DOM id of the field's primary control — the same id <see cref="Fields.FormFieldBase.FieldId"/>
/// was rendered with, namespaced to this <see cref="FormRenderer"/> instance, so
/// <c>href="#{FieldDomId}"</c> always resolves within the instance the summary belongs to.
/// </param>
/// <param name="Message">
/// The remedy-worded validation message to show for this field.
/// </param>
/// <remarks>
/// Public only because it is <see cref="ErrorSummary"/>'s parameter type, and every
/// Razor-file-backed component's parameters must be at least as accessible as the component
/// itself — see the remarks on <see cref="ErrorSummary"/>. Composed of two plain strings, so it
/// carries nothing that could fail the agnosticism architecture test.
/// </remarks>
public readonly record struct ErrorSummaryEntry(string FieldDomId, string Message);
