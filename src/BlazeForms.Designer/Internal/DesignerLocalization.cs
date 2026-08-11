using System.Collections;
using System.Globalization;
using System.Resources;
using BlazeForms.Resources;
using Microsoft.Extensions.Localization;

namespace BlazeForms.Internal;

/// <summary>
/// The designer's own, host-immune <see cref="IStringLocalizer{T}"/> for its chrome strings
/// (pane titles, palette group and node-type names, the search box's label, the "Phase 2" badge —
/// PRD §12). Resolves directly against <c>BlazeForms.Designer</c>'s embedded
/// <c>Resources/DesignerStrings.resx</c> through a <see cref="ResourceManager"/> keyed to a fixed
/// base name and this assembly, rather than through the ambient DI-configured
/// <c>IStringLocalizer&lt;T&gt;</c> pipeline a host resolves via <c>[Inject]</c>. That pipeline's
/// default resource-manager factory falls back to <c>typeof(DesignerStrings).FullName</c> as the
/// base name only when the host has not configured its own
/// <c>LocalizationOptions.ResourcesPath</c> — a host that <em>has</em> (very common, for its own
/// resources) shifts the base name for every <c>IStringLocalizer&lt;T&gt;</c> the DI container
/// resolves, this one included, so the designer's chrome would render its resource keys verbatim
/// ("PaneTitleCanvas") instead of the localized text. Fixing the base name here, independent of
/// <c>LocalizationOptions</c>, makes that failure mode unreachable — the same reasoning
/// <c>BlazeForms.Renderer</c>'s <c>RendererLocalization</c> documents for the renderer's chrome.
/// </summary>
/// <remarks>
/// Still speaks <see cref="IStringLocalizer{T}"/> — the abstraction PRD §12 promises — so a
/// community satellite assembly built against the same base name
/// (<c>BlazeForms.Resources.DesignerStrings</c>) still localizes this chrome; only the *lookup
/// path* is fixed, not the ability to add cultures.
/// </remarks>
internal sealed class DesignerLocalization : IStringLocalizer<DesignerStrings>
{
    private const string BaseName = "BlazeForms.Resources.DesignerStrings";

    private static readonly ResourceManager Manager = new(BaseName, typeof(DesignerStrings).Assembly);

    /// <summary>
    /// The single shared instance every designer chrome component resolves its strings through
    /// (PRD §12). Stateless and safe to share — a <see cref="ResourceManager"/> lookup carries no
    /// per-caller state of its own.
    /// </summary>
    public static IStringLocalizer<DesignerStrings> Shared { get; } = new DesignerLocalization();

    private DesignerLocalization()
    {
    }

    /// <inheritdoc />
    public LocalizedString this[string name] => Resolve(name, []);

    /// <inheritdoc />
    public LocalizedString this[string name, params object[] arguments] => Resolve(name, arguments);

    /// <inheritdoc />
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var resourceSet = Manager.GetResourceSet(CultureInfo.CurrentUICulture, createIfNotExists: true, tryParents: includeParentCultures);

        if (resourceSet is null)
        {
            yield break;
        }

        foreach (DictionaryEntry entry in resourceSet)
        {
            yield return new LocalizedString((string)entry.Key, (string)entry.Value!);
        }
    }

    private static LocalizedString Resolve(string name, object[] arguments)
    {
        var template = Manager.GetString(name, CultureInfo.CurrentUICulture);

        if (template is null)
        {
            return new LocalizedString(name, name, resourceNotFound: true);
        }

        // CurrentUICulture selects *which* template comes back from the resource set above;
        // CurrentCulture is the correct provider for formatting the {0}/{1} arguments into it
        // (CA1305) -- the two culture properties serve genuinely different purposes here.
        var value = arguments.Length == 0 ? template : string.Format(CultureInfo.CurrentCulture, template, arguments);
        return new LocalizedString(name, value, resourceNotFound: false);
    }
}
