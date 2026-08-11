namespace BlazeForms.Resources;

/// <summary>
/// The marker type <c>IStringLocalizer&lt;RendererStrings&gt;</c> resolves against (PRD §12).
/// Carries no members of its own — its only job is to give the resource manager convention a
/// type identity to key off — and its resx sibling, <c>RendererStrings.resx</c>, ships the
/// English chrome strings every renderer component reads through it.
/// </summary>
/// <remarks>
/// Internal, not public: every consumer of this type lives inside <c>BlazeForms.Renderer</c> —
/// <see cref="BlazeForms.FormRenderer"/> and <see cref="BlazeForms.Fields.DateRangeField"/> each
/// resolve their chrome strings through
/// <see cref="BlazeForms.Internal.RendererLocalization.Shared"/>, so a host never names this type
/// directly. Keeping it internal also means it never has to satisfy the agnosticism architecture
/// test on its own account (that test only walks <em>exported</em> types).
/// <para>
/// This type's namespace is deliberately <c>BlazeForms.Resources</c> — matching the folder its
/// resx sibling sits in — because <see cref="BlazeForms.Internal.RendererLocalization"/> builds
/// its <see cref="System.Resources.ResourceManager"/> from the fixed base name
/// <c>BlazeForms.Resources.RendererStrings</c> plus this type's own assembly, independent of any
/// DI-configured <c>LocalizationOptions.ResourcesPath</c>. A host that sets a global
/// <c>ResourcesPath</c> for its own resources still shifts how the ambient, DI-resolved
/// <c>IStringLocalizer&lt;T&gt;</c> behaves for every <em>other</em> type in the process — but
/// never for this one, since nothing here goes through that pipeline.
/// </para>
/// </remarks>
internal sealed class RendererStrings
{
    private RendererStrings()
    {
    }
}
