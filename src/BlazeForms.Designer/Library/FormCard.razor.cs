using System.ComponentModel;

namespace BlazeForms.Library;

/// <summary>
/// Declares <see cref="FormCard"/>'s sealedness and browsability; its markup, parameters, and
/// logic live in the collocated <c>FormCard.razor</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class FormCard;
