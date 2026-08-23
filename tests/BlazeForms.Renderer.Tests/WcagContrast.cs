using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Computes WCAG 2.x relative-luminance contrast ratios between two sRGB hex colors —
/// the same formula <see cref="ThemeContrastTests"/> uses to prove the default theme's token
/// pairs meet 1.4.3/1.4.11. Public (not <see langword="internal"/>) because
/// <c>BlazeForms.Designer.Tests</c> calls it too, via that project's own reference to this one
/// (<c>DesignerContrastTests</c>) — mirroring the source-side dependency direction
/// (<c>BlazeForms.Designer</c> depends on <c>BlazeForms.Renderer</c>, PRD §9) rather than
/// duplicating the luminance math per project. Increment C's high-contrast theme test is this
/// helper's third caller, still from here.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "BlazeForms.Designer.Tests calls this via a project reference to this assembly -- CA1515 has no way to know that cross-project use is intended, and InternalsVisibleTo would just trade one visibility escape hatch for another.")]
public static class WcagContrast
{
    /// <summary>
    /// Returns the WCAG contrast ratio between two <c>#rrggbb</c> colors, in the range 1:1
    /// (identical) to 21:1 (black on white).
    /// </summary>
    public static double Ratio(string hexA, string hexB)
    {
        var luminanceA = RelativeLuminance(hexA);
        var luminanceB = RelativeLuminance(hexB);

        var lighter = Math.Max(luminanceA, luminanceB);
        var darker = Math.Min(luminanceA, luminanceB);

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        var (r, g, b) = ParseHex(hex);

        return 0.2126 * Linearize(r) + 0.7152 * Linearize(g) + 0.0722 * Linearize(b);
    }

    private static double Linearize(double channel) =>
        channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

    private static (double R, double G, double B) ParseHex(string hex)
    {
        var value = hex.TrimStart('#');

        var r = byte.Parse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = byte.Parse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = byte.Parse(value[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        return (r / 255.0, g / 255.0, b / 255.0);
    }
}
