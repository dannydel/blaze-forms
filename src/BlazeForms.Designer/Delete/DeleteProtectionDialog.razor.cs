using System.Diagnostics.CodeAnalysis;
using BlazeForms.Definitions;
using BlazeForms.Designer;
using BlazeForms.Expressions;
using BlazeForms.Internal;
using BlazeForms.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace BlazeForms.Delete;

/// <summary>
/// The delete-protection warning dialog (PRD §4.1): raised by <c>DesignerCanvas</c>'s own
/// <c>Delete</c>-key handling whenever <see cref="ExpressionDependencyAnalysis.ReferencesTo"/>
/// finds at least one live reference to the node about to be deleted, naming every one of them --
/// a node's own visibility rule, a validation rule's target, or a validation rule's own
/// expression -- so the author knows exactly what breaks before choosing "Delete anyway" (which
/// deletes through <see cref="DesignerEditContext.DeleteNode"/> same as an unreferenced delete,
/// leaving any now-dangling reference for the linter's own <c>FR-03</c> to flag) or Cancel/<c>Esc</c>
/// (which touches nothing at all).
/// </summary>
/// <remarks>
/// <para>
/// <b>A fresh instance every open.</b> <c>DesignerCanvas</c> mounts this component only while its
/// own dialog is showing, tearing it down entirely on close -- the same "no persistent instance to
/// toggle" shape <c>MoveToPositionDialog</c> documents for itself, for the same reasons.
/// </para>
/// <para>
/// <b>Focus trap.</b> This dialog is <c>role="dialog" aria-modal="true"</c>, labelled by its own
/// title, and moves real DOM focus to its Cancel button (not the destructive "Delete anyway"
/// button) the moment its first render lands -- defaulting focus to the safe action is deliberate
/// here, unlike <c>MoveToPositionDialog</c>'s own first-control default, because a stray
/// <c>Enter</c> reaching whichever control already has focus must never itself delete anything.
/// The collocated <c>DeleteProtectionDialog.razor.js</c> module cycles <c>Tab</c>/<c>Shift+Tab</c>
/// between this dialog's two buttons, the exact same genuine-platform-gap rationale
/// <c>MoveToPositionDialog.razor.js</c> documents for its own trap. <c>Escape</c> needs no JS at
/// all, the same as that dialog's.
/// </para>
/// <para>
/// <b>Focus destination after closing.</b> Both <see cref="DeleteAnywayAsync"/> and
/// <see cref="CancelAsync"/> only ever raise <see cref="OnClosed"/> -- moving real DOM focus back
/// to the canvas is <c>DesignerCanvas</c>'s own job, the same split <c>MoveToPositionDialog</c>
/// documents for itself.
/// </para>
/// </remarks>
public partial class DeleteProtectionDialog : ComponentBase, IAsyncDisposable
{
    /// <summary>
    /// The static web asset path this component imports its focus-trap JS module from, following
    /// the same <c>_content/{assembly}/{path}</c> convention every collocated Razor Class Library
    /// JS file resolves to. <c>internal</c> so a test can set up the module mock against the exact
    /// path this component requests.
    /// </summary>
    internal const string ModulePath = "./_content/BlazeForms.Designer/Delete/DeleteProtectionDialog.razor.js";

    private readonly string _instanceId = "bf-delete-dialog-" + Guid.NewGuid().ToString("n");
    private ElementReference _dialogElement;
    private ElementReference _cancelButtonElement;
    private IJSObjectReference? _module;
    private IJSObjectReference? _focusTrapHandle;
    private IReadOnlyList<string> _referenceDescriptions = [];
    private string _nodeLabel = string.Empty;
    private bool _disposed;

    /// <summary>
    /// The mutation engine this dialog deletes the node through, and searches for references
    /// against.
    /// </summary>
    [Parameter, EditorRequired]
    public DesignerEditContext EditContext { get; set; } = default!;

    /// <summary>
    /// The node being asked about. Every live reference to it, from
    /// <see cref="ExpressionDependencyAnalysis.ReferencesTo"/>, is described in this dialog's own
    /// list.
    /// </summary>
    [Parameter, EditorRequired]
    public string NodeId { get; set; } = default!;

    /// <summary>
    /// Raised once this dialog should close -- after "Delete anyway" (<see cref="DeleteAnywayAsync"/>)
    /// or a cancel (<see cref="CancelAsync"/>). Carries no payload, the same reason
    /// <c>MoveToPositionDialog.OnClosed</c> does not: <c>DesignerCanvas</c> already knows which
    /// node it opened this dialog for, and whether it still exists is trivially checkable.
    /// </summary>
    [Parameter]
    public EventCallback OnClosed { get; set; }

    /// <summary>
    /// Used only to import <see cref="ModulePath"/>'s focus-trap module.
    /// </summary>
    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    private string TitleId => _instanceId + "-title";

    /// <inheritdoc/>
    protected override void OnInitialized()
    {
        var definition = EditContext.Draft.Definition;
        var node = definition.FindNode(NodeId)
            ?? throw new InvalidOperationException($"No node '{NodeId}' was found in the current draft.");

        _nodeLabel = NodeLabel(node);
        _referenceDescriptions = [.. ExpressionDependencyAnalysis.ReferencesTo(definition, NodeId).Select(site => Describe(site, definition))];
    }

    /// <inheritdoc/>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "A Blazor lifecycle method must resume on the renderer's synchronization context, not a captured-context-free one, so it can safely schedule the next render.")]
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _module = await JSRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
        _focusTrapHandle = await _module.InvokeAsync<IJSObjectReference>("attachFocusTrap", _dialogElement);
        await _cancelButtonElement.FocusAsync();
    }

    /// <summary>
    /// Detaches the focus-trap listener and disposes the imported module. Safe to call more than
    /// once.
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

        if (_focusTrapHandle is not null)
        {
            await _focusTrapHandle.InvokeVoidAsync("dispose");
            await _focusTrapHandle.DisposeAsync();
            _focusTrapHandle = null;
        }

        if (_module is not null)
        {
            await _module.DisposeAsync();
            _module = null;
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Whether the focus-trap JS module has been imported. <c>internal</c>, not <c>private</c>,
    /// solely so a test can prove <see cref="DisposeAsync"/> actually disposes it -- the same
    /// rationale <c>MoveToPositionDialog.HasImportedModule</c> gives for itself.
    /// </summary>
    internal bool HasImportedModule => _module is not null;

    /// <summary>
    /// Deletes the node anyway -- through the exact same <see cref="DesignerEditContext.DeleteNode"/>
    /// every unreferenced delete already uses -- then closes. Whatever reference this dialog just
    /// named becomes dangling the instant this runs; the linter's own <c>FR-03</c> rule is what
    /// reports that on the very next lint pass, not this dialog.
    /// </summary>
    private Task DeleteAnywayAsync()
    {
        EditContext.DeleteNode(NodeId);
        return OnClosed.InvokeAsync();
    }

    /// <summary>
    /// Cancels -- the <c>Esc</c> path, and the visible Cancel button's -- without touching
    /// <see cref="EditContext"/> at all, then closes.
    /// </summary>
    private Task CancelAsync() => OnClosed.InvokeAsync();

    private Task OnDialogKeyDown(KeyboardEventArgs e) =>
        string.Equals(e.Key, "Escape", StringComparison.Ordinal) ? CancelAsync() : Task.CompletedTask;

    private static string NodeLabel(FormNode node) =>
        node.Label ?? Localizer["UntitledNodeLabel", Localizer[$"NodeType{node.Type}"].Value].Value;

    /// <summary>
    /// Renders one <see cref="ReferenceSite"/> as the plain-language line this dialog's list shows
    /// for it, resolving whichever node or rule actually carries the reference -- PRD §4.1's
    /// "naming every reference", including a validation rule's own reference, which
    /// <see cref="ReferenceKind.ValidationTarget"/> and <see cref="ReferenceKind.ValidationExpression"/>
    /// both describe by the rule's own message.
    /// </summary>
    private static string Describe(ReferenceSite site, FormDefinition definition)
    {
        switch (site.Kind)
        {
            case ReferenceKind.Visibility:
                var referencingNode = definition.FindNode(site.ReferencingNodeId!)
                    ?? throw new InvalidOperationException($"No node '{site.ReferencingNodeId}' was found in the current draft.");
                return Localizer["DeleteProtectionReferenceVisibility", NodeLabel(referencingNode)].Value;

            case ReferenceKind.ValidationTarget:
                return Localizer["DeleteProtectionReferenceValidationTarget", site.ReferencingRule!.Message].Value;

            case ReferenceKind.ValidationExpression:
                var targetNode = definition.FindNode(site.ReferencingRule!.Target);
                var targetLabel = targetNode is null ? site.ReferencingRule.Target : NodeLabel(targetNode);
                return Localizer["DeleteProtectionReferenceValidationExpression", targetLabel, site.ReferencingRule.Message].Value;

            default:
                throw new NotSupportedException($"Unknown reference kind '{site.Kind}'.");
        }
    }
}
