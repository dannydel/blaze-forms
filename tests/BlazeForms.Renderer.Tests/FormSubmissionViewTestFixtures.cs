using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Hosting;
using BlazeForms.Serialization;
using BlazeForms.Versioning;

namespace BlazeForms.Renderer.Tests;

/// <summary>
/// Shared <see cref="FormDefinition"/>/<see cref="FormVersion"/>/<see cref="FormSubmissionEnvelope"/>
/// fixtures for <see cref="FormSubmissionView"/> tests, mirroring the style of
/// <see cref="FormRendererTestFixtures"/>.
/// </summary>
internal static class FormSubmissionViewTestFixtures
{
    /// <summary>
    /// Wraps a definition as the captured <see cref="FormVersion"/> a submission was taken
    /// against.
    /// </summary>
    internal static FormVersion ToVersion(
        FormDefinition definition,
        int version = 3,
        FormLifecycleState state = FormLifecycleState.Published) => new()
    {
        FormId = definition.Id,
        Version = version,
        State = state,
        Definition = definition,
    };

    /// <summary>
    /// Builds a submission envelope from a plain answer dictionary, converting through
    /// <see cref="FormValues.ToJsonValues"/> exactly as <see cref="FormRenderer.BuildSubmissionEnvelope"/>
    /// does.
    /// </summary>
    internal static FormSubmissionEnvelope BuildEnvelope(
        FormDefinition definition,
        int definitionVersion,
        IReadOnlyDictionary<string, object?> values)
    {
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        return new FormSubmissionEnvelope
        {
            SubmissionId = "sub-test",
            FormId = definition.Id,
            DefinitionVersion = definitionVersion,
            StartedAt = startedAt,
            SubmittedAt = startedAt.AddMinutes(4),
            Values = FormValues.ToJsonValues(values),
        };
    }

    /// <summary>
    /// Builds a submission envelope the way <see cref="FormRenderer.BuildSubmissionEnvelope"/>
    /// really does, instead of <see cref="BuildEnvelope"/>'s already-clean hand-picked value set:
    /// runs <see cref="VisibilityEvaluator.FilterToVisible"/> against a raw, fill-time value set
    /// -- one that may still carry an answer to a node a controller currently hides -- before
    /// converting the settled result through <see cref="FormValues.ToJsonValues"/>. Only this
    /// path actually exercises the a→b→c fixed-point reconstruction
    /// <see cref="FormSubmissionView"/>'s whole hidden-vs-empty distinction rests on.
    /// </summary>
    internal static FormSubmissionEnvelope BuildEnvelopeFromRawValues(
        FormDefinition definition,
        int definitionVersion,
        IReadOnlyDictionary<string, object?> rawValues)
    {
        var visiblePayload = VisibilityEvaluator.FilterToVisible(definition, rawValues);
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        return new FormSubmissionEnvelope
        {
            SubmissionId = "sub-test-raw",
            FormId = definition.Id,
            DefinitionVersion = definitionVersion,
            StartedAt = startedAt,
            SubmittedAt = startedAt.AddMinutes(4),
            Values = FormValues.ToJsonValues(visiblePayload),
        };
    }

    /// <summary>
    /// Two pages, two sections, exercising every row state <see cref="FormSubmissionView"/> can
    /// render (PRD §4.3): a plain text answer, a single-choice answer that must resolve to its
    /// option's label, a multi-select answer resolving to every selected label, a boolean
    /// (<c>show-extra</c>) left untouched despite being visible throughout, a field
    /// (<c>secret</c>) hidden by logic because <c>show-extra</c> was never set true, and a field
    /// (<c>notes</c>) that is visible throughout but never answered.
    /// </summary>
    internal static FormDefinition SubmissionViewDefinition { get; } = new()
    {
        Id = "form-submission-view",
        Name = "Submission view",
        Pages =
        [
            new FormPage
            {
                Id = "page-1",
                Title = "About you",
                Sections =
                [
                    new FormSection
                    {
                        Id = "section-1",
                        Title = "Your details",
                        Nodes =
                        [
                            new FormNode { Id = "name", Type = NodeType.Text, Label = "Name" },
                            new FormNode
                            {
                                Id = "state",
                                Type = NodeType.Select,
                                Label = "State",
                                Options =
                                [
                                    new FormOption { Value = "wa", Label = "Washington" },
                                    new FormOption { Value = "or", Label = "Oregon" },
                                ],
                            },
                            new FormNode
                            {
                                Id = "topics",
                                Type = NodeType.CheckboxGroup,
                                Label = "Topics",
                                Options =
                                [
                                    new FormOption { Value = "a", Label = "Topic A" },
                                    new FormOption { Value = "b", Label = "Topic B" },
                                ],
                            },
                            new FormNode { Id = "show-extra", Type = NodeType.Boolean, Label = "Show extra?" },
                            new FormNode
                            {
                                Id = "secret",
                                Type = NodeType.Text,
                                Label = "Secret detail",
                                VisibleWhen = new ConditionGroup
                                {
                                    Conditions = [new Condition { Field = "show-extra", Operator = ConditionOperator.IsTrue }],
                                },
                            },
                        ],
                    },
                ],
            },
            new FormPage
            {
                Id = "page-2",
                Title = "Notes",
                Sections =
                [
                    new FormSection
                    {
                        Id = "section-2",
                        Title = "Additional notes",
                        Nodes = [new FormNode { Id = "notes", Type = NodeType.TextArea, Label = "Notes" }],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// The answers <see cref="SubmissionViewDefinition"/>'s tests capture — <c>show-extra</c>,
    /// <c>secret</c>, and <c>notes</c> are deliberately absent (PRD §9).
    /// </summary>
    internal static IReadOnlyDictionary<string, object?> SubmissionViewValues { get; } = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["name"] = "Ada Lovelace",
        ["state"] = "wa",
        ["topics"] = new List<string> { "a", "b" },
    };

    /// <summary>
    /// One untitled page (<see cref="FormPage.Title"/> is <see langword="null"/>), for the
    /// empty-heading/unnamed-landmark regression test.
    /// </summary>
    internal static FormDefinition NullPageTitleDefinition { get; } = new()
    {
        Id = "form-null-page-title",
        Name = "Null page title",
        Pages =
        [
            new FormPage
            {
                Id = "page-1",
                Title = null,
                Sections =
                [
                    new FormSection
                    {
                        Id = "section-1",
                        Nodes = [new FormNode { Id = "name", Type = NodeType.Text, Label = "Name" }],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// One section whose nodes interleave two captured headings among the input nodes they
    /// introduce, for the "no <c>div</c> inside a <c>dl</c>" regression test: a headingless run
    /// ("before" the first heading), then two heading-led runs, the second of which introduces no
    /// fields at all.
    /// </summary>
    internal static FormDefinition HeadingGroupedDefinition { get; } = new()
    {
        Id = "form-heading-grouped",
        Name = "Heading grouped",
        Pages =
        [
            new FormPage
            {
                Id = "page-1",
                Title = "Page one",
                Sections =
                [
                    new FormSection
                    {
                        Id = "section-1",
                        Nodes =
                        [
                            new FormNode { Id = "before", Type = NodeType.Text, Label = "Before" },
                            new FormNode { Id = "h1", Type = NodeType.Heading, Label = "Intro" },
                            new FormNode { Id = "after-h1", Type = NodeType.Text, Label = "After intro" },
                            new FormNode { Id = "h2", Type = NodeType.Heading, Label = "Trailing" },
                        ],
                    },
                ],
            },
        ],
    };
}
