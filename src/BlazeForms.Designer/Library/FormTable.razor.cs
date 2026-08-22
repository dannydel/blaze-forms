using System.ComponentModel;

namespace BlazeForms.Library;

/// <summary>
/// Declares <see cref="FormTable"/>'s sealedness and browsability; its markup, parameters, and
/// logic live in the collocated <c>FormTable.razor</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class FormTable;
