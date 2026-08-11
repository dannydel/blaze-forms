using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="KeyboardHelpDialog"/> (Phase 7, PRD §4.1's "discoverable via an in-app
/// dialog"): it opens focus-trapped and labelled, lists the discoverable commands, and closes on
/// <c>Esc</c> or its own Close button, raising <see cref="KeyboardHelpDialog.OnClosed"/> either
/// way. Coverage of the shell's own Help button that opens it lives in <c>FormDesignerTests</c>.
/// </summary>
public sealed class KeyboardHelpDialogTests : DesignerTestContext
{
    [Fact]
    public void RendersAsAFocusLabelledModalDialogListingCommands()
    {
        var cut = Render<KeyboardHelpDialog>();

        var dialog = cut.Find("div.bf-keyboard-help");
        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.Equal(cut.Find("h2").Id, dialog.GetAttribute("aria-labelledby"));

        Assert.Contains("Ctrl+D", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Ctrl+Z", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Ctrl+Shift+Z", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Ctrl+M", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EscClosesAndRaisesOnClosed()
    {
        var closed = false;
        var cut = Render<KeyboardHelpDialog>(p => p.Add(d => d.OnClosed, () => closed = true));

        await cut.Find("div.bf-keyboard-help").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.True(closed);
    }

    [Fact]
    public async Task CloseButtonClosesAndRaisesOnClosed()
    {
        var closed = false;
        var cut = Render<KeyboardHelpDialog>(p => p.Add(d => d.OnClosed, () => closed = true));

        await cut.Find("button.bf-keyboard-help__button").ClickAsync(new MouseEventArgs());

        Assert.True(closed);
    }

    [Fact]
    public void TheFocusTrapModuleIsImportedAndDisposed()
    {
        var module = JSInterop.SetupModule(KeyboardHelpDialog.ModulePath);
        var cut = Render<KeyboardHelpDialog>();

        cut.WaitForAssertion(() => Assert.True(cut.Instance.HasImportedModule));
        module.VerifyInvoke("attachFocusTrap");
        JSInterop.VerifyFocusAsyncInvoke();
    }
}
