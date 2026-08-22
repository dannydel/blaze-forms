using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace BlazeForms;

/// <summary>
/// The class the Razor SDK generates from <c>_Imports.razor</c>. The file carries only
/// <c>@using</c> directives and the class is never rendered, but the SDK emits it as
/// <c>public partial</c> unconditionally (.NET 10, no opt-out), so this declaration hides it
/// from IntelliSense. It cannot be sealed: the generated <c>protected Execute()</c> would then
/// be CS0628.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "The name is fixed by the Razor SDK's codegen for _Imports.razor; this declaration only adjusts the generated type's browsability.")]
public partial class _Imports;
