using System.Diagnostics.CodeAnalysis;
using Bunit;
using Microsoft.JSInterop;

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
/// <remarks>
/// <see cref="JSRuntimeMode.Loose"/> is the default here, not <c>Strict</c>: any test that
/// renders <see cref="Canvas.DesignerCanvas"/> (directly, or nested inside
/// <see cref="FormDesigner"/>) triggers its first-render import of its collocated scroll-
/// suppression module, and a test with no reason to care about that JS call should not have to
/// configure or await it just to avoid bUnit's strict-mode exception -- the same rationale
/// <c>FormSubmissionViewExportTests</c> gives for its own loose-mode choice. A test that does
/// care (<c>DesignerCanvasTests</c>' own module-import/dispose coverage) still calls
/// <c>JSInterop.SetupModule</c> to get a handle it can assert against.
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "Every test class in this project is public by the existing convention here, and a public class cannot derive from an internal base type (CS0060) — this base has to be public to match.")]
public abstract class DesignerTestContext : BunitContext
{
    protected DesignerTestContext() => JSInterop.Mode = JSRuntimeMode.Loose;
}
