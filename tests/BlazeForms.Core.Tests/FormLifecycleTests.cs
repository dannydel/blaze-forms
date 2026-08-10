using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Versioning;

namespace BlazeForms.Core.Tests;

/// <summary>
/// PRD §7 and AGENTS.md invariant #3: Draft → Published v1..vN → Retired, and nothing ever
/// mutates a published version.
/// </summary>
public sealed class FormLifecycleTests
{
    private static readonly DateTimeOffset PublishedAt = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    private static FormVersion Draft() => FormLifecycle.CreateDraft(TestDefinitions.RepresentativeDefinition);

    [Fact]
    public void ANewDraftIsUnpublished()
    {
        var draft = Draft();

        Assert.Equal(FormLifecycleState.Draft, draft.State);
        Assert.Equal(FormLifecycle.UnpublishedVersion, draft.Version);
        Assert.Equal(0, draft.Version);
        Assert.Equal("form-transport-enrollment", draft.FormId);
        Assert.Null(draft.PublishedAt);
        Assert.Null(draft.ChangeNote);
    }

    [Fact]
    public void PublishingProducesANewVersionAndLeavesTheDraftUntouched()
    {
        var draft = Draft();

        var published = FormLifecycle.Publish(draft, 1, "Initial release.", "ada", PublishedAt);

        Assert.NotSame(draft, published);
        Assert.Equal(FormLifecycleState.Draft, draft.State);
        Assert.Equal(0, draft.Version);

        Assert.Equal(FormLifecycleState.Published, published.State);
        Assert.Equal(1, published.Version);
        Assert.Equal("Initial release.", published.ChangeNote);
        Assert.Equal("ada", published.Author);
        Assert.Equal(PublishedAt, published.PublishedAt);
        Assert.Same(draft.Definition, published.Definition);
    }

    [Fact]
    public void PublishingRequiresAChangeNote()
    {
        var draft = Draft();

        Assert.Throws<ArgumentException>(() => FormLifecycle.Publish(draft, 1, "   ", "ada", PublishedAt));
        Assert.Throws<ArgumentNullException>(() => FormLifecycle.Publish(draft, 1, null!, "ada", PublishedAt));
    }

    [Fact]
    public void PublishingRequiresAnAuthorAndAPositiveVersion()
    {
        var draft = Draft();

        Assert.Throws<ArgumentException>(() => FormLifecycle.Publish(draft, 1, "Note.", " ", PublishedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => FormLifecycle.Publish(draft, 0, "Note.", "ada", PublishedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => FormLifecycle.Publish(draft, -1, "Note.", "ada", PublishedAt));
    }

    [Fact]
    public void OnlyADraftCanBePublished()
    {
        var published = FormLifecycle.Publish(Draft(), 1, "Initial release.", "ada", PublishedAt);

        Assert.Throws<InvalidOperationException>(() => FormLifecycle.Publish(published, 2, "Again.", "ada", PublishedAt));
    }

    [Fact]
    public void RetiringOnlyAppliesToAPublishedVersion()
    {
        var published = FormLifecycle.Publish(Draft(), 1, "Initial release.", "ada", PublishedAt);
        var retiredAt = PublishedAt.AddDays(30);

        var retired = FormLifecycle.Retire(published, retiredAt);

        Assert.Equal(FormLifecycleState.Retired, retired.State);
        Assert.Equal(retiredAt, retired.RetiredAt);
        Assert.Equal(1, retired.Version);
        Assert.Equal(FormLifecycleState.Published, published.State);

        Assert.Throws<InvalidOperationException>(() => FormLifecycle.Retire(Draft(), retiredAt));
        Assert.Throws<InvalidOperationException>(() => FormLifecycle.Retire(retired, retiredAt));
    }

    [Fact]
    public void RestoringAnOldVersionMeansStartingANewDraftFromItsContent()
    {
        var published = FormLifecycle.Publish(Draft(), 3, "Third release.", "ada", PublishedAt);

        var revision = FormLifecycle.ReviseAsDraft(published);

        Assert.Equal(FormLifecycleState.Draft, revision.State);
        Assert.Equal(FormLifecycle.UnpublishedVersion, revision.Version);
        Assert.Null(revision.ChangeNote);
        Assert.Null(revision.PublishedAt);
        Assert.Null(revision.RetiredAt);
        Assert.Same(published.Definition, revision.Definition);
        Assert.Equal(3, published.Version);
    }

    [Fact]
    public void AVersionIsAValueAndCopyingItDoesNotDisturbTheOriginal()
    {
        var published = FormLifecycle.Publish(Draft(), 1, "Initial release.", "ada", PublishedAt);

        var copy = published with { ChangeNote = "Rewritten note." };

        Assert.Equal("Initial release.", published.ChangeNote);
        Assert.Equal("Rewritten note.", copy.ChangeNote);
        Assert.NotEqual(published, copy);
        Assert.Equal(published, published with { });
    }

    [Fact]
    public void TheLifecycleHelpersRejectNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => FormLifecycle.CreateDraft(null!));
        Assert.Throws<ArgumentNullException>(() => FormLifecycle.Publish(null!, 1, "Note.", "ada", PublishedAt));
        Assert.Throws<ArgumentNullException>(() => FormLifecycle.Retire(null!, PublishedAt));
        Assert.Throws<ArgumentNullException>(() => FormLifecycle.ReviseAsDraft(null!));
    }

    [Fact]
    public void ADefinitionIsAValueSoEditsAlwaysProduceACopy()
    {
        var original = TestDefinitions.RepresentativeDefinition;

        var renamed = original with { Name = "Renamed" };

        Assert.Equal("Student transportation enrollment", original.Name);
        Assert.Equal("Renamed", renamed.Name);
        Assert.Equal(original.Pages, renamed.Pages);
    }

    [Fact]
    public void MutatingTheListACallerPassedInCannotReachAPublishedVersion()
    {
        var nodes = new List<FormNode> { new() { Id = "node-a", Type = NodeType.Text, Label = "A" } };
        var sections = new List<FormSection> { new() { Id = "section-1", Nodes = nodes } };
        var pages = new List<FormPage> { new() { Id = "page-1", Sections = sections } };
        var rules = new List<ValidationRule>();

        var published = FormLifecycle.Publish(
            FormLifecycle.CreateDraft(new FormDefinition
            {
                Id = "form-a",
                Name = "Form A",
                Pages = pages,
                ValidationRules = rules,
            }),
            1,
            "Initial release.",
            "ada",
            PublishedAt);

        // Everything a caller could still be holding a reference to.
        nodes.Add(new FormNode { Id = "node-smuggled", Type = NodeType.Text, Label = "Smuggled" });
        sections.Add(new FormSection { Id = "section-smuggled" });
        pages.Add(new FormPage { Id = "page-smuggled" });
        rules.Add(new ValidationRule
        {
            Target = "node-a",
            Message = "Smuggled.",
            Expression = new ConditionGroup(),
        });

        Assert.Single(published.Definition.Pages);
        Assert.Single(published.Definition.Pages[0].Sections);
        Assert.Single(published.Definition.Pages[0].Sections[0].Nodes);
        Assert.Empty(published.Definition.ValidationRules);
        Assert.Null(published.Definition.FindNode("node-smuggled"));
    }

    [Fact]
    public void MutatingTheOptionsAndConditionsACallerPassedInCannotReachTheNode()
    {
        var options = new List<FormOption> { new() { Value = "yes", Label = "Yes" } };
        var children = new List<FormNode> { new() { Id = "node-child", Type = NodeType.Text } };
        var conditions = new List<Condition>
        {
            new() { Field = "node-trigger", Operator = ConditionOperator.Is, Value = "yes" },
        };

        var node = new FormNode
        {
            Id = "node-a",
            Type = NodeType.Select,
            Options = options,
            Children = children,
            VisibleWhen = new ConditionGroup { Conditions = conditions },
        };

        options.Add(new FormOption { Value = "smuggled", Label = "Smuggled" });
        children.Add(new FormNode { Id = "node-smuggled", Type = NodeType.Text });
        conditions.Add(new Condition { Field = "node-other", Operator = ConditionOperator.IsBlank });

        Assert.Single(node.Options);
        Assert.Single(node.Children);
        Assert.Single(node.VisibleWhen!.Conditions);
    }

    [Fact]
    public void GeneratedIdentifiersAreUniqueAndPrefixed()
    {
        Assert.StartsWith("node-", FormIds.NewNodeId(), StringComparison.Ordinal);
        Assert.StartsWith("form-", FormIds.NewFormId(), StringComparison.Ordinal);
        Assert.StartsWith("page-", FormIds.NewPageId(), StringComparison.Ordinal);
        Assert.StartsWith("section-", FormIds.NewSectionId(), StringComparison.Ordinal);
        Assert.StartsWith("sub-", FormIds.NewSubmissionId(), StringComparison.Ordinal);
        Assert.NotEqual(FormIds.NewNodeId(), FormIds.NewNodeId());
    }
}
