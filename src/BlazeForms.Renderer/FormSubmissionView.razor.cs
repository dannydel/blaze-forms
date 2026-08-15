using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using BlazeForms.Definitions;
using BlazeForms.Expressions;
using BlazeForms.Fields.Internal;
using BlazeForms.Hosting;
using BlazeForms.Internal;
using BlazeForms.Resources;
using BlazeForms.Serialization;
using BlazeForms.Versioning;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace BlazeForms;

/// <summary>
/// Renders one submission read-only, against the exact definition version it was captured
/// against (PRD §4.3, §7): pages and sections laid out exactly as the captured
/// <see cref="Versioning.FormVersion.Definition"/> structures them, one label/value row per input
/// node. A field the respondent never saw because logic hid it renders the localized "Not
/// applicable" text; a field the respondent saw but left empty renders an em-dash placeholder —
/// the two are indistinguishable from key presence alone (the submission envelope is pure to
/// PRD §9: hidden <em>and</em> visible-but-untouched answers are both simply absent), so this
/// component tells them apart by re-evaluating <see cref="VisibilityEvaluator"/> against the
/// captured definition and the captured answers, not by checking whether a key exists.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rendering fixed point.</b> <see cref="FormRenderer.BuildSubmissionEnvelope"/> settles
/// visibility to a fixed point before filtering the payload (a chain a → b → c must not leak c's
/// answer once a hides b), so <see cref="Hosting.FormSubmissionEnvelope.Values"/> already holds
/// only the answers that were visible at that fixed point. Re-running
/// <see cref="VisibilityEvaluator.GetVisibleNodes"/> once, directly against those same answers,
/// therefore reproduces the exact fill-time visible set without a second settling pass.
/// </para>
/// <para>
/// <b>Static content.</b> A captured <see cref="NodeType.Heading"/> still carries structural
/// meaning — it is muted, non-outline text here rather than a genuine <c>h2</c>-<c>h4</c>, so it
/// never competes with the page/section headings this component already emits for its own
/// document outline. <see cref="NodeType.Paragraph"/>, <see cref="NodeType.Callout"/>, and
/// <see cref="NodeType.Divider"/> are fill-time author guidance, not submission data, and are
/// omitted entirely to keep a reviewer's read focused on what the respondent answered.
/// </para>
/// <para>
/// <b>JSON export.</b> Downloading a file is a genuine platform gap browsers give no
/// pure-Blazor path around, so <see cref="ExportJsonAsync"/> is the one place this component
/// reaches for JS — a collocated ES module (<c>FormSubmissionView.razor.js</c>) imported lazily,
/// on the respondent's first click, never eagerly during a lifecycle method. That laziness is
/// also what keeps the import prerender-safe without a first-render flag to track: nothing can
/// click a button before the component is interactive, so the import can never run during a
/// non-interactive prerendered pass.
/// </para>
/// </remarks>
public partial class FormSubmissionView : ComponentBase, IAsyncDisposable
{
    /// <summary>
    /// The static web asset path this component imports its JS module from, following the same
    /// <c>_content/{assembly}/{path}</c> convention every collocated Razor Class Library JS file
    /// resolves to. <c>internal</c> so <c>FormSubmissionViewExportTests</c> can set up the module
    /// mock against the exact path this component requests.
    /// </summary>
    internal const string ModulePath = "./_content/BlazeForms.Renderer/FormSubmissionView.razor.js";

    private readonly string _instanceId = "bf-submission-" + Guid.NewGuid().ToString("n");
    private IReadOnlyDictionary<string, object?> _values = new Dictionary<string, object?>(StringComparer.Ordinal);
    private HashSet<string> _visibleNodeIds = new(StringComparer.Ordinal);
    private IJSObjectReference? _module;
    private Task<IJSObjectReference>? _moduleImport;
    private bool _disposed;

    /// <summary>
    /// The submission to render. Its <see cref="Hosting.FormSubmissionEnvelope.Values"/> are read
    /// against <see cref="Version"/>'s definition, never against whatever the form's latest
    /// published version happens to be (PRD §7).
    /// </summary>
    [Parameter, EditorRequired]
    public FormSubmissionEnvelope Envelope { get; set; } = default!;

    /// <summary>
    /// The exact definition version <see cref="Envelope"/> was captured against. The host loads
    /// this itself, typically via <c>IFormDefinitionStore</c>, and hands it in — this component
    /// never resolves a store on its own, so it stays usable wherever a host already has the
    /// version in hand.
    /// </summary>
    [Parameter, EditorRequired]
    public FormVersion Version { get; set; } = default!;

    /// <summary>
    /// The form's current latest published version number, or <see langword="null"/> when the
    /// host has not computed one. A value greater than
    /// <see cref="Hosting.FormSubmissionEnvelope.DefinitionVersion"/> shows the superseded-version
    /// notice (PRD §4.3, §7); <see langword="null"/>, or a value that is not greater, shows
    /// nothing extra — the rendered value rows are identical either way (success criterion #5).
    /// </summary>
    [Parameter]
    public int? LatestPublishedVersion { get; set; }

    /// <summary>
    /// This component's own, host-immune localizer for its chrome strings — the same
    /// <see cref="RendererLocalization.Shared"/> instance <see cref="FormRenderer"/> resolves
    /// through, and for the same reason (see its remarks): a DI-injected
    /// <c>IStringLocalizer&lt;RendererStrings&gt;</c> is unsafe against a host's own
    /// <c>LocalizationOptions.ResourcesPath</c>.
    /// </summary>
    private static IStringLocalizer<RendererStrings> Localizer => RendererLocalization.Shared;

    /// <summary>
    /// Used only inside <see cref="GetModuleAsync"/>, and only once the respondent has actually
    /// clicked the export button — never during a lifecycle method, so this parameter's presence
    /// never forces a host to run this component under an interactive render mode just to render
    /// the read-only view itself.
    /// </summary>
    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    private FormDefinition Definition => Version.Definition;

    /// <summary>
    /// The localized superseded-version notice text, or <see langword="null"/> when
    /// <see cref="LatestPublishedVersion"/> is unset or no greater than the version this
    /// submission was captured against.
    /// </summary>
    private string? SupersededNoticeText =>
        LatestPublishedVersion is int latest && latest > Envelope.DefinitionVersion
            ? Localizer["SubmissionSupersededBody", latest, Envelope.DefinitionVersion].Value
            : null;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        _values = FormValues.FromJsonValues(Envelope.Values);
        _visibleNodeIds = VisibilityEvaluator.GetVisibleNodes(Definition, _values)
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);
    }

    private string PageHeadingId(FormPage page) => $"{_instanceId}-{page.Id}";

    /// <summary>
    /// The page heading text, falling back to a positional placeholder — the same one
    /// <see cref="FormRenderer"/>'s progress list uses for the same case — when the captured
    /// <see cref="FormPage.Title"/> is <see langword="null"/>. Without this, an untitled page
    /// would emit an empty <c>h2</c> (an axe empty-heading violation) whose enclosing
    /// <c>&lt;section aria-labelledby&gt;</c> would in turn have no accessible name
    /// (AGENTS.md invariant #4).
    /// </summary>
    private static string PageHeadingText(FormPage page, int pageIndex) =>
        page.Title ?? Localizer["SubmissionPageFallbackTitle", pageIndex + 1].Value;

    /// <summary>
    /// Splits one section's nodes into the runs <c>FormSubmissionView.razor</c> renders: a
    /// captured <see cref="NodeType.Heading"/> starts a new run rather than joining the previous
    /// one's <c>&lt;dl&gt;</c>, because <c>&lt;dl&gt;</c>'s only valid children are <c>dt</c>/
    /// <c>dd</c> pairs (optionally wrapped in a <c>div</c> that itself contains only <c>dt</c>/
    /// <c>dd</c>) — a heading's own text-only <c>div</c> cannot legally sit among them. A group
    /// with no fields (a trailing heading, or two headings back to back) renders its heading with
    /// no empty <c>&lt;dl&gt;</c> beneath it.
    /// </summary>
    /// <remarks>
    /// This iterates <see cref="FormSection.Nodes"/> flatly and never recurses
    /// <see cref="FormNode.Children"/> — correct for P1, where no shipped node type carries
    /// children, but a P2 container/repeating node type will need this method (and its razor
    /// caller) to recurse rather than being mistaken for an intentional permanent flat read.
    /// </remarks>
    private static List<FormNodeGroup> BuildNodeGroups(FormSection section)
    {
        var groups = new List<FormNodeGroup>();
        FormNode? heading = null;
        var fields = new List<FormNode>();

        foreach (var node in section.Nodes)
        {
            if (node.Type == NodeType.Heading)
            {
                if (heading is not null || fields.Count > 0)
                {
                    groups.Add(new FormNodeGroup(heading, fields));
                }

                heading = node;
                fields = [];
                continue;
            }

            if (!FormSchema.IsStaticNode(node.Type))
            {
                fields.Add(node);
            }
        }

        if (heading is not null || fields.Count > 0)
        {
            groups.Add(new FormNodeGroup(heading, fields));
        }

        return groups;
    }

    /// <summary>
    /// Decides how one input node's row renders: hidden by fill-time logic, visible but
    /// unanswered, or visible with an answer to format (PRD §4.3).
    /// </summary>
    private FieldDisplay BuildDisplay(FormNode node)
    {
        if (!_visibleNodeIds.Contains(node.Id))
        {
            return new FieldDisplay(FieldDisplayKind.NotApplicable, "");
        }

        if (node.Type == NodeType.Calc)
        {
            return BuildCalcDisplay(node);
        }

        if (!_values.TryGetValue(node.Id, out var value) || value is null)
        {
            return new FieldDisplay(FieldDisplayKind.Empty, "");
        }

        var text = FormatFieldValue(node, value);

        return string.IsNullOrEmpty(text)
            ? new FieldDisplay(FieldDisplayKind.Empty, "")
            : new FieldDisplay(FieldDisplayKind.Value, text);
    }

    /// <summary>
    /// Builds a visible <see cref="NodeType.Calc"/> node's row: the value
    /// <see cref="FormRenderer.RecomputeCalculations"/> captured into the envelope, formatted per
    /// <see cref="FormNode.Calculation"/>'s <see cref="CalcFormat"/> exactly as the live
    /// <c>CalcField</c> shows it (decision log D-D), or the same author-authored placeholder
    /// <c>CalcField</c> falls back to when nothing was captured — a pre-engine (v1) envelope, a
    /// calc node the engine could not resolve (a reference cycle, a blank operand), or one the
    /// author gave no calculation at all.
    /// </summary>
    private FieldDisplay BuildCalcDisplay(FormNode node)
    {
        if (_values.TryGetValue(node.Id, out var raw) && raw is not null)
        {
            var format = node.Calculation?.Format ?? CalcFormat.Number;
            var formatted = CalcDisplayFormatter.Format(NormalizeCapturedCalcValue(raw, format), format);

            if (formatted is not null)
            {
                return new FieldDisplay(FieldDisplayKind.Value, formatted);
            }
        }

        var placeholderText = node.Placeholder ?? "";
        return string.IsNullOrEmpty(placeholderText)
            ? new FieldDisplay(FieldDisplayKind.Empty, "")
            : new FieldDisplay(FieldDisplayKind.Value, placeholderText);
    }

    /// <summary>
    /// Reverses the one lossy leg of the envelope round trip <see cref="CalcDisplayFormatter"/>
    /// needs to know about: <see cref="Serialization.FormValues.FromJsonValues"/> hands a captured
    /// date back as its raw ISO-8601 <see cref="string"/>, not a <see cref="DateOnly"/>, because
    /// JSON itself has no date type. Every other captured calc value already comes back as the
    /// <see cref="decimal"/> <see cref="CalcDisplayFormatter.Format"/> expects.
    /// </summary>
    private static object? NormalizeCapturedCalcValue(object raw, CalcFormat format) =>
        raw is string isoDate && format == CalcFormat.Date
            ? DateOnly.TryParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null
            : raw;

    private static string ResolveDisplayText(FieldDisplay display) => display.Kind switch
    {
        FieldDisplayKind.NotApplicable => Localizer["SubmissionNotApplicable"].Value,
        FieldDisplayKind.Empty => Localizer["SubmissionEmptyValue"].Value,
        _ => display.Text,
    };

    private static string ValueCssClass(FieldDisplayKind kind) => kind switch
    {
        FieldDisplayKind.NotApplicable => "bf-submission__value bf-submission__value--not-applicable",
        FieldDisplayKind.Empty => "bf-submission__value bf-submission__value--empty",
        _ => "bf-submission__value",
    };

    /// <summary>
    /// Renders one node's captured answer as respondent-facing plain text — never markup
    /// (AGENTS.md invariant #6) — in the CLR shape <c>Fields/Internal/FieldValueConventions.cs</c>
    /// documents for the node's type, the same shape every shipped field component writes and
    /// <see cref="Serialization.FormValues.FromJsonValues"/> hands back.
    /// </summary>
    private static string FormatFieldValue(FormNode node, object value) => node.Type switch
    {
        NodeType.Text or NodeType.TextArea or NodeType.Email or NodeType.Phone => value as string ?? value.ToString() ?? "",
        NodeType.Number or NodeType.Currency => FormatNumber(value),
        NodeType.Date => FormatDate(value as string),
        NodeType.DateRange => FormatDateRange(value as IReadOnlyList<string>),
        NodeType.Select or NodeType.Radio or NodeType.YesNo => ResolveOptionLabel(node, value as string),
        NodeType.CheckboxGroup => FormatCheckboxGroup(node, value as IReadOnlyList<string>),
        NodeType.Boolean => value is true ? Localizer["SubmissionBooleanYes"].Value : Localizer["SubmissionBooleanNo"].Value,
        _ => value.ToString() ?? "",
    };

    private static string ResolveOptionLabel(FormNode node, string? value)
    {
        if (value is null)
        {
            return "";
        }

        // Stored values stay stable when labels are edited (AGENTS.md invariant #5) -- falling
        // back to the raw stored value when nothing matches is defensive against a definition
        // whose options changed after this submission was captured, not an expected path.
        return node.Options.FirstOrDefault(option => string.Equals(option.Value, value, StringComparison.Ordinal))?.Label
            ?? value;
    }

    private static string FormatCheckboxGroup(FormNode node, IReadOnlyList<string>? selections)
    {
        if (selections is null || selections.Count == 0)
        {
            return "";
        }

        return string.Join(", ", selections.Select(selection => ResolveOptionLabel(node, selection)));
    }

    // Parse-invariant, display-current split: every stored value here came off the wire in a
    // fixed, culture-agnostic shape (a JSON number, an ISO 8601 date string) -- InvariantCulture
    // is correct, and the only correct choice, for reading that shape back into a CLR value.
    // Formatting that value back out for a reviewer to read is a different operation entirely,
    // and must use CurrentCulture so the reviewer sees their own culture's numeral and short-date
    // conventions instead of always US-style output (localization gap fix).

    private static string FormatNumber(object value) => value switch
    {
        decimal number => number.ToString(CultureInfo.CurrentCulture),
        double number => number.ToString(CultureInfo.CurrentCulture),
        _ => value.ToString() ?? "",
    };

    private static string FormatDate(string? isoDate)
    {
        if (string.IsNullOrEmpty(isoDate))
        {
            return "";
        }

        return DateOnly.TryParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("d", CultureInfo.CurrentCulture)
            : isoDate;
    }

    private static string FormatDateRange(IReadOnlyList<string>? parts)
    {
        if (parts is null || parts.Count == 0)
        {
            return "";
        }

        var start = FormatDate(parts.Count > 0 ? parts[0] : null);
        var end = FormatDate(parts.Count > 1 ? parts[1] : null);

        return start.Length == 0 && end.Length == 0 ? "" : $"{start} – {end}";
    }

    /// <summary>
    /// Downloads <see cref="Envelope"/> as an indented JSON file (PRD §4.3), importing the
    /// collocated JS module on the respondent's first click rather than eagerly — see this type's
    /// remarks for why that is also what keeps the import prerender-safe.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor event handler must resume on the renderer's synchronization context, not a captured-context-free one, so it can safely schedule the next render.")]
    private async Task ExportJsonAsync()
    {
        var json = FormJson.SerializeEnvelope(Envelope, indented: true);
        var module = await GetModuleAsync();
        await module.InvokeVoidAsync("downloadSubmissionJson", BuildExportFileName(), json);
    }

    /// <summary>
    /// Returns the shared module-import task, starting it on the first call and handing every
    /// later call the same in-flight (or completed) task rather than a fresh one -- a rapid
    /// second click while the first import is still awaiting its round trip must not import the
    /// module twice and orphan one <see cref="IJSObjectReference"/>. Not itself <c>async</c>, so
    /// the caching assignment below runs synchronously, before this component's single-threaded
    /// renderer could ever schedule a second call in between.
    /// </summary>
    private Task<IJSObjectReference> GetModuleAsync() => _moduleImport ??= ImportModuleAsync();

    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "Called only from GetModuleAsync, which is reached only from ExportJsonAsync, which must itself resume on the renderer's synchronization context.")]
    private async Task<IJSObjectReference> ImportModuleAsync()
    {
        var module = await JSRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);

        if (_disposed)
        {
            // The component was disposed while this import was still in flight -- DisposeAsync
            // already ran and saw a null _module, so it will never dispose this reference.
            // Dispose it here instead rather than assigning it and leaking it.
            await module.DisposeAsync();
            return module;
        }

        _module = module;
        return module;
    }

    private string BuildExportFileName() => $"{Envelope.FormId}-{Envelope.SubmissionId}.json";

    /// <summary>
    /// Whether the JS module has been imported. <c>internal</c>, not <c>private</c>, solely so
    /// <c>FormSubmissionViewExportTests</c> can prove the module is actually disposed by
    /// <see cref="DisposeAsync"/> — bUnit's JS-interop mock does not itself simulate a module
    /// reference becoming unusable once disposed, so the only deterministic way to prove this
    /// component disposes it is to observe the field going back to <see langword="null"/>, the
    /// same rationale <see cref="FormRenderer.SubmitAsync"/> gives for its own internal test seam.
    /// </summary>
    internal bool HasImportedModule => _module is not null;

    /// <summary>
    /// Disposes the JS module reference once imported (PRD's Blazor JS-interop convention,
    /// AGENTS.md). A no-op when the respondent never clicked export, since nothing was ever
    /// imported.
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "Blazor disposes a component on its own renderer's synchronization context, same as every other lifecycle method in this file.")]
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_module is not null)
        {
            await _module.DisposeAsync();
            _module = null;
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Which of the three states a captured input node's row renders in (PRD §4.3).
    /// </summary>
    private enum FieldDisplayKind
    {
        /// <summary>
        /// The respondent never saw this node — it was hidden by logic at fill time.
        /// </summary>
        NotApplicable,

        /// <summary>
        /// The respondent saw this node but left it unanswered.
        /// </summary>
        Empty,

        /// <summary>
        /// The respondent saw this node and gave it an answer, formatted in
        /// <see cref="FieldDisplay.Text"/>.
        /// </summary>
        Value,
    }

    /// <summary>
    /// One node's resolved display state, computed once per render by
    /// <see cref="BuildDisplay"/> rather than re-derived by the markup that consumes it.
    /// </summary>
    private readonly record struct FieldDisplay(FieldDisplayKind Kind, string Text);

    /// <summary>
    /// One run of a section's nodes, computed once per render by <see cref="BuildNodeGroups"/>:
    /// an optional heading (<see langword="null"/> for the run before a section's first heading,
    /// if any) followed by the input nodes it introduces, up to the next heading or the end of
    /// the section.
    /// </summary>
    private readonly record struct FormNodeGroup(FormNode? Heading, IReadOnlyList<FormNode> Fields)
    {
        /// <summary>
        /// A stable <c>@key</c> for this run: the heading node's own <see cref="FormNode.Id"/>
        /// when one introduces it, or a fixed sentinel for the single headingless run a section
        /// may start with.
        /// </summary>
        public string Key => Heading?.Id ?? "lead";
    }
}
