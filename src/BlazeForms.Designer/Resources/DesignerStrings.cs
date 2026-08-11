namespace BlazeForms.Resources;

/// <summary>
/// The marker type <c>IStringLocalizer&lt;DesignerStrings&gt;</c> resolves against (PRD §12).
/// Carries no members of its own — its only job is to give the resource manager convention a
/// type identity to key off — and its resx sibling, <c>DesignerStrings.resx</c>, ships the
/// English chrome strings every designer component reads through it.
/// </summary>
/// <remarks>
/// Internal, not public: every consumer of this type lives inside <c>BlazeForms.Designer</c> —
/// <see cref="BlazeForms.FormDesigner"/> and <see cref="BlazeForms.Palette.FieldPalette"/> each
/// resolve their chrome strings through
/// <see cref="BlazeForms.Internal.DesignerLocalization.Shared"/>, so a host never names this type
/// directly. Keeping it internal also means it never has to satisfy the agnosticism architecture
/// test on its own account (that test only walks <em>exported</em> types).
/// <para>
/// This type's namespace is deliberately <c>BlazeForms.Resources</c> — the same namespace
/// <c>BlazeForms.Renderer</c>'s sibling <c>RendererStrings</c> uses, since the two live in
/// separate assemblies and a resource-manager base name only ever needs to be unique within one
/// assembly. Matching the folder its resx sibling sits in matters here because
/// <see cref="BlazeForms.Internal.DesignerLocalization"/> builds its
/// <see cref="System.Resources.ResourceManager"/> from the fixed base name
/// <c>BlazeForms.Resources.DesignerStrings</c> plus this type's own assembly, independent of any
/// DI-configured <c>LocalizationOptions.ResourcesPath</c>. A host that sets a global
/// <c>ResourcesPath</c> for its own resources still shifts how the ambient, DI-resolved
/// <c>IStringLocalizer&lt;T&gt;</c> behaves for every <em>other</em> type in the process — but
/// never for this one, since nothing here goes through that pipeline.
/// </para>
/// </remarks>
internal sealed class DesignerStrings
{
    private DesignerStrings()
    {
    }
}
