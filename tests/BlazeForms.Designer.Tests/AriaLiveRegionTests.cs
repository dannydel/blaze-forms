using BlazeForms.Definitions;
using BlazeForms.Hosting.InMemory;
using BlazeForms.Versioning;
using Bunit;

namespace BlazeForms.Designer.Tests;

/// <summary>
/// Covers <see cref="AriaLiveRegion"/>: it renders as a polite status region, speaks the latest
/// <see cref="DesignerEditContext.Announced"/> message, updates when a further mutation announces
/// a new one, and unsubscribes cleanly on dispose (PRD §4.1, §11; AGENTS.md Blazor standards).
/// </summary>
public sealed class AriaLiveRegionTests : DesignerTestContext
{
    private static DesignerEditContext CreateContext() => new(
        FormLifecycle.CreateDraft(DesignerTestFixtures.TwoSectionDefinition("form-1")),
        new InMemoryFormDefinitionStore());

    [Fact]
    public async Task RendersAsAPoliteStatusRegionInitiallyEmpty()
    {
        await using var context = CreateContext();

        var cut = Render<AriaLiveRegion>(p => p.Add(r => r.EditContext, context));

        var region = cut.Find("[role='status']");
        Assert.Equal("polite", region.GetAttribute("aria-live"));
        Assert.Equal(string.Empty, region.TextContent);
    }

    [Fact]
    public async Task SpeaksTheLatestAnnouncementAndUpdatesOnANewOne()
    {
        await using var context = CreateContext();
        var cut = Render<AriaLiveRegion>(p => p.Add(r => r.EditContext, context));

        context.AddNode(NodeType.Text, "section-1");

        cut.WaitForAssertion(() => Assert.Contains("Added", cut.Find("[role='status']").TextContent, StringComparison.Ordinal));
        var firstMessage = cut.Find("[role='status']").TextContent;

        context.DeleteNode("node-a");

        cut.WaitForAssertion(() =>
            Assert.NotEqual(firstMessage, cut.Find("[role='status']").TextContent));
        Assert.Contains("Deleted", cut.Find("[role='status']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSameMessageAnnouncedTwiceInARowStillChangesTheRegionBothTimes()
    {
        await using var context = CreateContext();
        var cut = Render<AriaLiveRegion>(p => p.Add(r => r.EditContext, context));

        // Duplicating "node-a" twice in a row describes the untouched original both times, so
        // DesignerEditContext.Announced fires with the exact same message text on each call.
        context.DuplicateNode("node-a");
        cut.WaitForAssertion(() =>
            Assert.Contains("Duplicated 'Field A'.", cut.Find("[role='status']").TextContent, StringComparison.Ordinal));
        var afterFirst = cut.Find("[role='status']").TextContent;

        context.DuplicateNode("node-a");

        cut.WaitForAssertion(() => Assert.NotEqual(afterFirst, cut.Find("[role='status']").TextContent));
        Assert.Contains("Duplicated 'Field A'.", cut.Find("[role='status']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsubscribesFromTheContextOnDispose()
    {
        await using var context = CreateContext();
        var cut = Render<AriaLiveRegion>(p => p.Add(r => r.EditContext, context));

        await cut.Instance.DisposeAsync();

        // With no subscriber left, mutating the context must not throw even though the component
        // that used to react to it is gone.
        context.AddNode(NodeType.Text, "section-1");
    }

    [Fact]
    public async Task DisposeIsIdempotent()
    {
        await using var context = CreateContext();
        var cut = Render<AriaLiveRegion>(p => p.Add(r => r.EditContext, context));

        await cut.Instance.DisposeAsync();
        await cut.Instance.DisposeAsync();
    }
}
