using System.Diagnostics.CodeAnalysis;
using Bunit;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// The shared <see cref="BunitContext"/> base for every test that renders
/// <see cref="FormRenderer"/> or <see cref="Fields.DateRangeField"/>. Historically this registered
/// <c>Services.AddLocalization()</c> so those components' DI-injected
/// <c>IStringLocalizer&lt;RendererStrings&gt;</c> resolved against the real shipped English resx.
/// Now that both resolve their chrome strings through the internal, host-immune
/// <c>BlazeForms.Internal.RendererLocalization.Shared</c> instead (PRD §12 —
/// <c>FormRendererValidationTests</c>'s localization-insulation tests prove a host's own DI
/// localization state no longer matters to either), no registration is required here for either
/// to render correctly. This base stays only so every derived test class keeps one shared place
/// for bUnit setup, rather than every one of them deriving from <see cref="BunitContext"/>
/// directly for no reason.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "Every test class in this project is public by the existing convention here, and a public class cannot derive from an internal base type (CS0060) — this base has to be public to match.")]
public abstract class RendererTestContext : BunitContext
{
}
