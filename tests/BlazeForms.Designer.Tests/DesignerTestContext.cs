using System.Diagnostics.CodeAnalysis;
using Bunit;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// The shared <see cref="BunitContext"/> base for every test that renders <see cref="FormDesigner"/>
/// or <see cref="Palette.FieldPalette"/>. Both resolve their chrome strings through the internal,
/// host-immune <c>BlazeForms.Internal.DesignerLocalization.Shared</c> instead of a DI-injected
/// <c>IStringLocalizer&lt;DesignerStrings&gt;</c> (PRD §12), so no localization registration is
/// required here for either to render correctly. This base stays only so every derived test class
/// keeps one shared place for bUnit setup, rather than every one of them deriving from
/// <see cref="BunitContext"/> directly for no reason — mirroring
/// <c>BlazeForms.Renderer.Tests.RendererTestContext</c>.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "Every test class in this project is public by the existing convention here, and a public class cannot derive from an internal base type (CS0060) — this base has to be public to match.")]
public abstract class DesignerTestContext : BunitContext
{
}
