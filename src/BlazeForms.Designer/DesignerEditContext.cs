using System.Diagnostics.CodeAnalysis;
using BlazeForms.Definitions;
using BlazeForms.Designer.Internal;
using BlazeForms.Expressions;
using BlazeForms.Hosting;
using BlazeForms.Internal;
using BlazeForms.Resources;
using BlazeForms.Versioning;
using Microsoft.Extensions.Localization;

namespace BlazeForms.Designer;

/// <summary>
/// The mutation engine every designer editing surface sits on top of: one instance per
/// <see cref="FormDesigner"/>, holding the working draft, the undo/redo history, the current
/// selection, and the debounced autosave that persists it (PRD §4.1, §7, §11). Every mutation
/// method rebuilds <see cref="Draft"/>'s definition immutably via
/// <see cref="Internal.DefinitionMutations"/> (AGENTS.md invariant #3), pushes the prior state
/// onto the undo stack (capped at 50 -- PRD §4.1), moves <see cref="Selection"/> to the node the
/// mutation actually affected, and raises exactly one plain-language announcement on
/// <see cref="Announced"/> before finally raising <see cref="StateChanged"/>.
/// </summary>
/// <remarks>
/// <para>
/// This class owns no UI of its own -- <see cref="AriaLiveRegion"/> and the canvas/properties
/// components a later phase adds are all consumers of its public surface, not parts of it.
/// </para>
/// <para>
/// The <see cref="AutosaveScheduler"/> behind <see cref="Commit"/> persists the very first
/// mutation immediately and debounces every one after that, which is what turns Phase 1's "a
/// draft is persisted on the first edit, not on mere open" decision into an observable behaviour
/// (see <see cref="FormDesigner"/>'s remarks on <c>LoadDraftAsync</c>).
/// </para>
/// </remarks>
public sealed class DesignerEditContext : IAsyncDisposable
{
    /// <summary>
    /// How many prior states <see cref="_undoStack"/> keeps before it starts dropping the oldest
    /// one (PRD §4.1).
    /// </summary>
    private const int MaxUndoDepth = 50;

    private readonly List<EditSnapshot> _undoStack = [];
    private readonly List<EditSnapshot> _redoStack = [];
    private readonly AutosaveScheduler _autosave;
    private bool _disposed;

    /// <summary>
    /// Creates a context over an already-loaded draft.
    /// </summary>
    /// <param name="draft">
    /// The working draft this context edits. <see cref="FormDesigner"/> loads (or in-memory
    /// creates) this before constructing a context -- this constructor never touches
    /// <paramref name="store"/> itself; only <see cref="AutosaveScheduler.ScheduleSave"/> does,
    /// and only once the first mutation happens.
    /// </param>
    /// <param name="store">
    /// Where the autosave scheduler persists the draft after each mutation.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancelled by this context's owner to stop autosaving without waiting for
    /// <see cref="DisposeAsync"/> to run.
    /// </param>
    public DesignerEditContext(FormVersion draft, IFormDefinitionStore store, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(store);

        Draft = draft;
        Selection = DesignerSelection.None;
        _autosave = new AutosaveScheduler(store, externalCancellation: cancellationToken, onFailure: OnAutosaveFailed);
    }

    /// <summary>
    /// Raised after every mutation -- including <see cref="Undo"/> and <see cref="Redo"/> -- once
    /// <see cref="Draft"/> and <see cref="Selection"/> both reflect the new state. A consumer
    /// re-renders from this; it carries no payload because every piece of state it might need is
    /// already sitting on this context's own properties.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1003:Use generic event handler instances",
        Justification = "A plain, payload-free Action is the deliberate shape here -- every piece of state a subscriber needs already lives on this context's own properties, so a conventional EventHandler/EventArgs pair would carry a sender and an empty args object neither subscriber (FormDesigner's re-render, a future canvas) has any use for.")]
    public event Action? StateChanged;

    /// <summary>
    /// Raised exactly once per mutation with the plain-language message the aria-live region
    /// should speak (PRD §11). Raised after <see cref="StateChanged"/>, so a consumer that
    /// re-renders from the latter always sees the state the announcement is describing.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1003:Use generic event handler instances",
        Justification = "Action<DesignerAnnouncement> is the deliberate shape here -- DesignerAnnouncement already carries everything a subscriber (AriaLiveRegion) needs; wrapping it in an EventArgs subclass just to satisfy the conventional EventHandler<T> shape would add a type with no purpose beyond satisfying this rule.")]
    public event Action<DesignerAnnouncement>? Announced;

    /// <summary>
    /// Raised when a queued autosave actually fails to persist -- a host store's I/O error, a
    /// transient outage, and so on -- rather than the ordinary superseded-by-a-newer-edit
    /// cancellation <see cref="Internal.AutosaveScheduler"/> never raises this for. Carries only
    /// <see cref="Exception"/>, the BCL type every host's store can throw, so this stays as
    /// agnostic of any particular <see cref="IFormDefinitionStore"/> implementation as the rest
    /// of this class's public surface. This context stays fully usable after this fires -- the
    /// very next mutation queues its own autosave the same as ever, unaffected by the one that
    /// just failed; <see cref="FormDesigner"/> is what decides whether and how an author actually
    /// sees this (PRD §7, §11).
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1003:Use generic event handler instances",
        Justification = "A plain Action<Exception> is the deliberate shape here, for the same reason StateChanged and Announced above are -- the exception itself is everything a subscriber needs; wrapping it in an EventArgs subclass just to satisfy the conventional EventHandler<T> shape would add a type with no purpose beyond satisfying this rule.")]
    public event Action<Exception>? AutosaveFailed;

    /// <summary>
    /// The working draft as of the most recent mutation. Never the same instance a prior
    /// mutation held -- every mutation replaces this with a new <see cref="FormVersion"/> wrapping
    /// a new, immutably rebuilt <see cref="FormDefinition"/> (AGENTS.md invariant #3).
    /// </summary>
    public FormVersion Draft { get; private set; }

    /// <summary>
    /// What is currently selected, and why focus landed there. Starts at
    /// <see cref="DesignerSelection.None"/> and moves to the node, section, or page each mutation
    /// affected.
    /// </summary>
    public DesignerSelection Selection { get; private set; }

    /// <summary>
    /// Whether <see cref="Undo"/> would do anything right now.
    /// </summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>
    /// Whether <see cref="Redo"/> would do anything right now.
    /// </summary>
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// Whether this context has made at least one edit since it was constructed. Once
    /// <see langword="true"/>, stays <see langword="true"/> for the rest of this context's
    /// lifetime -- there is no "saved, so clean again" transition, because the draft this context
    /// holds is autosaved rather than saved on an explicit action a respondent-facing "unsaved
    /// changes" indicator would key off of.
    /// </summary>
    public bool IsDirty { get; private set; }

    /// <summary>
    /// The autosave task most recently queued by a mutation. <see langword="internal"/>, purely
    /// so a test can await a specific mutation's save deterministically instead of guessing at
    /// the debounce interval -- a host never needs to know when an autosave actually completes.
    /// </summary>
    internal Task PendingAutosave => _autosave.PendingSave;

    private static IStringLocalizer<DesignerStrings> Localizer => DesignerLocalization.Shared;

    /// <summary>
    /// Adds a new node to a section.
    /// </summary>
    /// <param name="type">
    /// The kind of node to add.
    /// </param>
    /// <param name="targetSectionId">
    /// The section to add it to.
    /// </param>
    /// <param name="index">
    /// The zero-based position within the section to insert at. <see langword="null"/> appends to
    /// the end.
    /// </param>
    public void AddNode(NodeType type, string targetSectionId, int? index = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSectionId);

        var node = new FormNode { Id = FormIds.NewNodeId(), Type = type };
        var updated = DefinitionMutations.InsertNode(Draft.Definition, targetSectionId, node, index);
        var located = DefinitionMutations.FindNodeLocation(updated, node.Id)!.Value;
        var page = updated.Pages[located.PageIndex];
        var section = page.Sections[located.SectionIndex];

        var selection = DesignerSelection.ForNode(node.Id, page.Id, section.Id, DesignerFocusIntent.NewNode);
        var message = Localizer["AnnouncementNodeAdded", NodeTypeLabel(type), SectionTitle(section)].Value;

        Commit(updated, selection, message);
    }

    /// <summary>
    /// Replaces a node's content in place -- everything a properties-panel edit touches (label,
    /// help, required, options, and so on). Never changes <see cref="FormNode.Id"/> or an
    /// existing <see cref="FormOption.Value"/> (AGENTS.md invariant #5); the caller is responsible
    /// for not attempting to.
    /// </summary>
    /// <param name="updated">
    /// The replacement node. <see cref="FormNode.Id"/> selects which existing node it replaces.
    /// </param>
    public void UpdateNode(FormNode updated)
    {
        ArgumentNullException.ThrowIfNull(updated);

        var newDefinition = DefinitionMutations.UpdateNode(Draft.Definition, updated);
        var located = DefinitionMutations.FindNodeLocation(newDefinition, updated.Id)!.Value;
        var page = newDefinition.Pages[located.PageIndex];
        var section = page.Sections[located.SectionIndex];

        // Editing a field's properties never moves focus away from wherever the author is
        // already editing -- there is nothing new to point focus at, unlike every other
        // mutation.
        var selection = DesignerSelection.ForNode(updated.Id, page.Id, section.Id, DesignerFocusIntent.None);
        var message = Localizer["AnnouncementNodeUpdated", DescribeNode(updated)].Value;

        Commit(newDefinition, selection, message);
    }

    /// <summary>
    /// Deletes a node. Focus falls back to its next sibling, its previous sibling if it was last,
    /// or the owning section itself if it was the section's only node (PRD §4.1, §11).
    /// </summary>
    /// <param name="nodeId">
    /// The node to delete.
    /// </param>
    public void DeleteNode(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var located = DefinitionMutations.FindNodeLocation(Draft.Definition, nodeId)
            ?? throw new ArgumentException($"No node '{nodeId}' was found in the current draft.", nameof(nodeId));

        var page = Draft.Definition.Pages[located.PageIndex];
        var section = page.Sections[located.SectionIndex];
        var siblings = section.Nodes;
        var deletedNode = siblings[located.NodeIndex];

        var updated = DefinitionMutations.RemoveNode(Draft.Definition, nodeId);

        var selection = siblings.Count == 1
            ? DesignerSelection.ForSection(page.Id, section.Id, DesignerFocusIntent.Neighbour)
            : DesignerSelection.ForNode(
                siblings[located.NodeIndex < siblings.Count - 1 ? located.NodeIndex + 1 : located.NodeIndex - 1].Id,
                page.Id,
                section.Id,
                DesignerFocusIntent.Neighbour);

        var message = Localizer["AnnouncementNodeDeleted", DescribeNode(deletedNode)].Value;

        Commit(updated, selection, message);
    }

    /// <summary>
    /// Duplicates a node, inserting the copy immediately after the original in the same section
    /// and selecting the copy.
    /// </summary>
    /// <param name="nodeId">
    /// The node to duplicate.
    /// </param>
    public void DuplicateNode(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var original = Draft.Definition.FindNode(nodeId)
            ?? throw new ArgumentException($"No node '{nodeId}' was found in the current draft.", nameof(nodeId));

        var (updated, duplicate) = DefinitionMutations.DuplicateNode(Draft.Definition, nodeId);
        var located = DefinitionMutations.FindNodeLocation(updated, duplicate.Id)!.Value;
        var page = updated.Pages[located.PageIndex];
        var section = page.Sections[located.SectionIndex];

        var selection = DesignerSelection.ForNode(duplicate.Id, page.Id, section.Id, DesignerFocusIntent.NewNode);
        var message = Localizer["AnnouncementNodeDuplicated", DescribeNode(original)].Value;

        Commit(updated, selection, message);
    }

    /// <summary>
    /// Appends a new, empty page and selects it.
    /// </summary>
    public void AddPage()
    {
        var page = new FormPage { Id = FormIds.NewPageId() };
        var updated = DefinitionMutations.AddPage(Draft.Definition, page);

        var selection = DesignerSelection.ForPage(page.Id, DesignerFocusIntent.NewNode);
        var message = Localizer["AnnouncementPageAdded", updated.Pages.Count].Value;

        Commit(updated, selection, message);
    }

    /// <summary>
    /// Appends a new, empty section to a page and selects it.
    /// </summary>
    /// <param name="pageId">
    /// The page to add the section to.
    /// </param>
    public void AddSection(string pageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);

        var section = new FormSection { Id = FormIds.NewSectionId() };
        var updated = DefinitionMutations.AddSection(Draft.Definition, pageId, section);
        var pageIndex = DefinitionMutations.FindPageIndex(updated, pageId)!.Value;
        var page = updated.Pages[pageIndex];

        var selection = DesignerSelection.ForSection(pageId, section.Id, DesignerFocusIntent.NewNode);
        var message = Localizer["AnnouncementSectionAdded", PageTitle(page, pageIndex)].Value;

        Commit(updated, selection, message);
    }

    /// <summary>
    /// Renames a page -- the page tab strip's double-click/F2 inline editor's path (PRD §4.1). A
    /// blank or whitespace-only <paramref name="title"/> normalizes to <see langword="null"/>,
    /// clearing the page back to its "Page N" fallback display name rather than storing an empty
    /// string as if it were a real title. Renaming to the page's own current title is a no-op: no
    /// history entry, no autosave, no announcement -- the same contract
    /// <see cref="MoveNodeWithinSection"/> already has for a move that does not actually move
    /// anything.
    /// </summary>
    /// <param name="pageId">
    /// The page to rename.
    /// </param>
    /// <param name="title">
    /// The new title. Blank or whitespace-only clears it to the fallback name.
    /// </param>
    public void RenamePage(string pageId, string? title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);

        var normalized = string.IsNullOrWhiteSpace(title) ? null : title;
        var updated = DefinitionMutations.RenamePage(Draft.Definition, pageId, normalized);

        // Unchanged (DefinitionMutations.RenamePage handed the same instance back): no history entry,
        // no announcement -- exactly the no-op contract a same-value edit should have.
        if (ReferenceEquals(updated, Draft.Definition))
        {
            return;
        }

        var pageIndex = DefinitionMutations.FindPageIndex(updated, pageId)!.Value;
        var page = updated.Pages[pageIndex];

        // A rename never moves focus onto the canvas -- the author stays on the tab strip. PageTabStrip
        // itself returns DOM focus to the renamed tab button locally (see its own OnAfterRenderAsync).
        var selection = DesignerSelection.ForPage(pageId, DesignerFocusIntent.None);
        var message = Localizer["AnnouncementPageRenamed", PageTitle(page, pageIndex)].Value;

        Commit(updated, selection, message);
    }

    /// <summary>
    /// Moves a node earlier or later within its own section -- the <c>Alt+↑/↓</c> keyboard path
    /// (PRD §4.1). A move that would go past either end of the section clamps there instead of
    /// wrapping, and does nothing at all (no history entry, no announcement) when the node is
    /// already at that end.
    /// </summary>
    /// <param name="nodeId">
    /// The node to move.
    /// </param>
    /// <param name="delta">
    /// The number of positions to move by; negative moves earlier, positive moves later.
    /// </param>
    public void MoveNodeWithinSection(string nodeId, int delta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var updated = DefinitionMutations.MoveNodeWithinSection(Draft.Definition, nodeId, delta);
        CommitMoveIfChanged(updated, nodeId);
    }

    /// <summary>
    /// Moves a node to a specific zero-based index, in its own section or a different one -- the
    /// drag-and-drop and <c>Alt+←/→</c> keyboard paths (PRD §4.1).
    /// </summary>
    /// <param name="nodeId">
    /// The node to move.
    /// </param>
    /// <param name="targetSectionId">
    /// The section the node should end up in.
    /// </param>
    /// <param name="index">
    /// The zero-based position within <paramref name="targetSectionId"/> to move to, clamped to
    /// its bounds.
    /// </param>
    public void MoveNodeAcrossSections(string nodeId, string targetSectionId, int index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSectionId);

        MoveNodeCore(nodeId, targetSectionId, index);
    }

    /// <summary>
    /// Moves a node to a specific one-based position -- the <c>Ctrl+M</c> "move to position"
    /// dialog's path (PRD §4.1), which presents positions to the author as "position 1", "position
    /// 2", and so on rather than zero-based indices.
    /// </summary>
    /// <param name="nodeId">
    /// The node to move.
    /// </param>
    /// <param name="sectionId">
    /// The section the node should end up in.
    /// </param>
    /// <param name="position">
    /// The one-based position within <paramref name="sectionId"/> to move to, clamped to its
    /// bounds.
    /// </param>
    public void MoveNodeToPosition(string nodeId, string sectionId, int position)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);

        MoveNodeCore(nodeId, sectionId, position - 1);
    }

    /// <summary>
    /// Replaces the form's cross-field validation rules wholesale -- the rule editor's save
    /// action (PRD §6).
    /// </summary>
    /// <param name="rules">
    /// The complete replacement rule set.
    /// </param>
    public void SetValidationRules(IReadOnlyList<ValidationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var updated = DefinitionMutations.SetValidationRules(Draft.Definition, rules);
        var message = Localizer["AnnouncementValidationRulesUpdated"].Value;

        Commit(updated, Selection with { Intent = DesignerFocusIntent.None }, message);
    }

    /// <summary>
    /// Moves the current selection without touching <see cref="Draft"/> -- the canvas's
    /// click-to-select and Enter-to-select paths (PRD §4.1, §11). Unlike every mutation method
    /// above, this never pushes an undo entry, never queues an autosave, and never raises
    /// <see cref="Announced"/>: moving which row is selected is not a document edit an author
    /// would want to reverse with Ctrl+Z, and speaking every selection change aloud would drown
    /// out the aria-live region for the structural changes that actually matter. Arrow-key
    /// roving-focus movement with no selection commit is deliberately not this method's
    /// concern -- a canvas is free to move real DOM focus among its own rows entirely locally,
    /// calling this only once an author actually commits to a row (a click, or Enter).
    /// </summary>
    /// <param name="selection">
    /// The new selection.
    /// </param>
    public void Select(DesignerSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        Selection = selection;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Reverts the most recent mutation still on the undo stack, restoring both the definition it
    /// replaced and the selection that was current at the time (PRD §4.1). A no-op when
    /// <see cref="CanUndo"/> is <see langword="false"/>.
    /// </summary>
    public void Undo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        var snapshot = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);

        _redoStack.Add(new EditSnapshot
        {
            Definition = Draft.Definition,
            Selection = Selection,
            Description = snapshot.Description,
        });

        Restore(snapshot, Localizer["AnnouncementUndid", snapshot.Description].Value);
    }

    /// <summary>
    /// Re-applies the most recent mutation <see cref="Undo"/> reverted, restoring both the
    /// definition it produced and the selection it left behind (PRD §4.1). A no-op when
    /// <see cref="CanRedo"/> is <see langword="false"/>.
    /// </summary>
    public void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        var snapshot = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);

        PushUndo(new EditSnapshot
        {
            Definition = Draft.Definition,
            Selection = Selection,
            Description = snapshot.Description,
        });

        Restore(snapshot, Localizer["AnnouncementRedid", snapshot.Description].Value);
    }

    /// <summary>
    /// Raises <see cref="Announced"/> directly, without touching <see cref="Draft"/>, the undo
    /// stack, or the autosave scheduler -- for an announcement that is not itself a definition
    /// mutation but still needs the aria-live region to speak, i.e. the autosave-failure
    /// announcement <see cref="FormDesigner"/> raises after <see cref="AutosaveFailed"/> fires.
    /// <see langword="internal"/> rather than <see langword="private"/> purely so
    /// <see cref="FormDesigner"/>, its only intended caller, can reach it.
    /// </summary>
    /// <param name="message">
    /// The localized, plain-language text to announce.
    /// </param>
    /// <param name="politeness">
    /// How urgently the live region should interrupt to speak this. Defaults to
    /// <see cref="AriaLivePoliteness.Polite"/>, the level every announcement in this phase uses.
    /// </param>
    internal void Announce(string message, AriaLivePoliteness politeness = AriaLivePoliteness.Polite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Announced?.Invoke(new DesignerAnnouncement { Message = message, Politeness = politeness });
    }

    /// <summary>
    /// Cancels any pending autosave and waits for it to actually stop. Safe to call more than
    /// once.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _autosave.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Shared tail for <see cref="MoveNodeAcrossSections"/> and <see cref="MoveNodeToPosition"/>:
    /// runs the underlying move and, only if it actually changed anything, commits it with the
    /// standard "Moved to position {n} of {m} in '{section}'" announcement (PRD §11).
    /// </summary>
    private void MoveNodeCore(string nodeId, string sectionId, int index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var updated = DefinitionMutations.MoveNode(Draft.Definition, nodeId, sectionId, index);
        CommitMoveIfChanged(updated, nodeId);
    }

    private void CommitMoveIfChanged(FormDefinition updated, string nodeId)
    {
        if (ReferenceEquals(updated, Draft.Definition))
        {
            return;
        }

        var located = DefinitionMutations.FindNodeLocation(updated, nodeId)!.Value;
        var page = updated.Pages[located.PageIndex];
        var section = page.Sections[located.SectionIndex];

        var selection = DesignerSelection.ForNode(nodeId, page.Id, section.Id, DesignerFocusIntent.Moved);
        var message = Localizer["AnnouncementNodeMoved", located.NodeIndex + 1, section.Nodes.Count, SectionTitle(section)].Value;

        Commit(updated, selection, message);
    }

    /// <summary>
    /// The one place every mutation actually lands: rebuilds <see cref="Draft"/> and
    /// <see cref="Selection"/> from the already-computed new state, pushes the state being left
    /// behind onto the undo stack (dropping the oldest entry past <see cref="MaxUndoDepth"/>),
    /// clears the redo stack, marks the context dirty, queues the autosave, and finally raises
    /// <see cref="StateChanged"/> then <see cref="Announced"/> -- in that order, so a consumer
    /// re-rendering from the former always sees the state the latter's message describes.
    /// </summary>
    private void Commit(FormDefinition definition, DesignerSelection selection, string message)
    {
        PushUndo(new EditSnapshot
        {
            Definition = Draft.Definition,
            Selection = Selection,
            Description = message,
        });
        _redoStack.Clear();

        Draft = Draft with { Definition = definition };
        Selection = selection;
        IsDirty = true;
        _autosave.ScheduleSave(Draft);

        StateChanged?.Invoke();
        Announced?.Invoke(new DesignerAnnouncement { Message = message });
    }

    /// <summary>
    /// The shared tail of <see cref="Undo"/> and <see cref="Redo"/>: applies a snapshot's
    /// definition and selection -- with <see cref="DesignerSelection.Intent"/> overridden to
    /// <see cref="DesignerFocusIntent.Restored"/>, regardless of what intent the mutation being
    /// undone or redone originally carried -- then queues the autosave and raises both events.
    /// Deliberately does not go through <see cref="Commit"/>: undo and redo move snapshots
    /// between the two stacks themselves rather than pushing a fresh one and clearing the other.
    /// </summary>
    private void Restore(EditSnapshot snapshot, string announcementMessage)
    {
        Draft = Draft with { Definition = snapshot.Definition };
        Selection = snapshot.Selection with { Intent = DesignerFocusIntent.Restored };
        IsDirty = true;
        _autosave.ScheduleSave(Draft);

        StateChanged?.Invoke();
        Announced?.Invoke(new DesignerAnnouncement { Message = announcementMessage });
    }

    /// <summary>
    /// The <see cref="Internal.AutosaveScheduler"/> failure callback wired up in the constructor
    /// -- forwards a genuine save failure to <see cref="AutosaveFailed"/> verbatim. This context
    /// does no more than that itself; deciding what an author sees is <see cref="FormDesigner"/>'s
    /// job.
    /// </summary>
    private void OnAutosaveFailed(Exception exception) => AutosaveFailed?.Invoke(exception);

    private void PushUndo(EditSnapshot snapshot)
    {
        _undoStack.Add(snapshot);

        if (_undoStack.Count > MaxUndoDepth)
        {
            _undoStack.RemoveAt(0);
        }
    }

    private static string NodeTypeLabel(NodeType type) => Localizer[$"NodeType{type}"].Value;

    private static string DescribeNode(FormNode node) => node.Label ?? NodeTypeLabel(node.Type);

    private static string SectionTitle(FormSection section) => section.Title ?? Localizer["UntitledSectionName"].Value;

    private static string PageTitle(FormPage page, int pageIndex) => page.Title ?? Localizer["PageFallbackTitle", pageIndex + 1].Value;
}
