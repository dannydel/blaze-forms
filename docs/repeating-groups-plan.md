# Repeating Groups — Implementation Plan (P2)

> Status: **approved, in progress**. Turns the schema-reserved `NodeType.Repeating` (PRD §5, §13)
> into a fillable repeating group: an author-defined set of child fields the respondent can add,
> remove, and reorder as rows. Modeled on `docs/calc-engine-plan.md` (3 dependency-ordered
> increments; the schema bump lands once, first). Honors D12 (one expression tree), D13 (drafts
> pinned to version), AGENTS invariants #2 (schema is a contract), #4 (keyboard + SR parity), #5
> (stable IDs key data).

## Resolved decisions (approved)

1. **Envelope shape — rowId + values everywhere.** Both the submission envelope and the draft store
   `"<groupId>": [ { "rowId": "row-…", "values": { "<childId>": <answer> } }, … ]`. One canonical,
   self-describing shape; rowIds are opaque GUIDs kept in the envelope for audit/diffing.
2. **Cross-row aggregation — deferred.** This slice ships per-row calc (line totals), per-row
   visibility/validation, and group min/max rows. A top-level calc referencing a row child is a
   **blocking lint (FR-04)**, never silently wrong. `sum-over-rows` is a future additive change (a
   new `CalcOperand` aggregate member or a new `CalcOperation`), riding a later schemaVersion bump.
3. **schemaVersion → 3** (one-time, Increment A). v1/v2 documents keep reading with the new
   properties null — no migration.
4. **Required semantics — one mechanism.** Hide the `Required` toggle for repeating groups in the
   properties panel; express "at least one row" as `MinRows ≥ 1`.
5. **One nesting level this slice.** The designer will not author a repeating group inside a
   repeating group (mutation guard + palette hides Repeating while scoped inside a group). The
   renderer's structural branch recurses, so an *imported* nested definition still renders defined-ly
   rather than throwing.

## Ground truth (verified in code before planning)

- **Schema.** `FormNode.Children` already exists, serializes (source-gen recursion), and every
  traversal descends into it (`FormDefinitionExtensions.EnumerateNodes`/`Flatten`,
  `VisibilityEvaluator`, `DefinitionMutations.CloneWithFreshIds`). `FormSchema.CurrentVersion = 2`;
  `ReservedNodeTypesBacking = [Repeating, File, Lookup]`; `IsInputNode(Repeating)` is `true`.
  `FormJson.ValidateDeclaredSchemaVersion` already accepts `1..CurrentVersion`.
- **Answers are flat.** `FormRenderer._values` is `Dictionary<string, object?>` keyed by node id;
  `FormValues.ToJsonValues/FromJsonValues` is an explicit switch over scalar shapes (+
  `IEnumerable<string>`); a JSON object round-trips as an opaque `JsonElement`. Envelope and draft
  values are both `IReadOnlyDictionary<string, JsonElement>`. Envelope purity: hidden answers are
  *absent* (PRD §9), enforced by `VisibilityEvaluator.FilterToVisible`'s shrink-only fixed point.
- **Evaluators are pure and flat.** `ConditionEvaluator`, `CalcEvaluator.EvaluateAll` (calc-id →
  value, topo order), `ExpressionDependencyAnalysis` all read one flat id→value map.
  Reference-by-bare-node-id is the whole addressing model.
- **Renderer.** `DefaultFieldComponents.Resolve(Repeating)` **throws**, pinned by
  `DefaultFieldComponentsTests.cs:82`. `FieldValueConventions.GetStoredClrType(Repeating)` → `null`.
  `FormRenderer.razor` renders `section.Nodes` flat; validation, errors, error-summary anchors,
  draft resume (`HydrateDraftValues`, incl. the `eab57cf` Date hydration), and `RecomputeCalculations`
  all live in `FormRenderer.razor.cs`. `FormSubmissionView.BuildNodeGroups` has a remark that a P2
  repeating type must make it recurse.
- **Designer.** Palette Advanced group = `[Calc, ..ReservedNodeTypes]` — un-reserving Repeating
  removes it from the palette unless added explicitly. Canvas is `listbox → group → option` (ARIA
  forbids deeper nesting). `DefinitionMutations` documents it never mutates `Children`;
  `FindNodeLocation` only searches top-level `Section.Nodes`. `DesignerEditContext` is the single
  mutation/undo surface (whole-definition mementos).
- **Behavior-pinning tests this slice changes:** `SerializationTests.cs:183–191` (18 P1 / 3
  reserved), `FieldPaletteTests` (Repeating disabled), `DefaultFieldComponentsTests.cs:82` (throw).

## Design

### Answer & envelope model
- New immutable Core value type `RepeatingRows` (ordered `IReadOnlyList<RepeatingRow>`);
  `RepeatingRow { string RowId; IReadOnlyDictionary<string, object?> Values }`; pure functional
  mutators `AddRow`, `RemoveRow(rowId)`, `MoveRow(rowId, delta)`, `SetValue(rowId, childId, value)`
  returning new instances (so `FormRenderer.SetValue(groupId, updatedRows)` needs no new pipeline).
  New file `src/BlazeForms.Core/Serialization/RepeatingRows.cs` next to `FormValues`.
- **Row identity:** `FormIds.NewRowId()` (`row-` prefix, opaque GUID). Machine-generated, immutable,
  never content-derived; keys `@key` diffing, per-row error/DOM ids, draft resume, reorder. Serialized
  in drafts (resume must rebind) and kept in the envelope.
- **`FormValues`:** `Write` gains a `RepeatingRows` case (array of `{rowId, values}` objects, inner
  values through the existing switch). `FromJsonElement` gains **strict** shape recognition: a JSON
  array whose every element is an object with exactly `rowId` (string) + `values` (object) → parses
  to `RepeatingRows`; anything else keeps today's behavior (string-array / opaque element).
- **Schema v3:** `FormNode` gains optional `MinRows` (int?), `MaxRows` (int?), `ItemLabel` (string?).
  `FormSchema.CurrentVersion = 3`. Keep `form-definition-v1.json` / `-v2.json`; add
  `form-definition-v3.json` (a representative group with a per-row calc + a within-row visibility
  rule), round-trip tests, and a pinned repeating-envelope JSON test. `FormJsonContext` picks up the
  new `FormNode` props automatically (source-gen); `RepeatingRows` is not a definition type so it is
  not registered there.

### Reference semantics — positional row scoping; aggregation out
- The expression tree is untouched (D12): a `Condition.Field`/`CalcOperand.Field` is still a bare
  node id. Scoping is decided by where the rule lives.
- **Within-row (IN):** a rule/calc on a node inside a group referencing a sibling child resolves
  against that row via a **row-scoped view** — outer flat values overlaid with the row's `Values` —
  handed to the unchanged evaluators. New internal `RowScope.Merge(outerValues, row)`; row-awareness
  lives in orchestration, not the evaluators (deepen, don't fork).
- **Row → outside (IN):** works through the merged view (outer values are the base layer).
- **Outside → inside a row (OUT, linted):** ambiguous (which row?) → **blocking lint FR-04**; the
  designer's field pickers don't offer those fields. Same rule forbids references between two
  different groups.
- **Per-row calc:** `CalcEvaluator.EvaluateAll` becomes repeating-aware behind its existing
  signature — for each group whose children include calc nodes, evaluate them per row (row-scoped
  view, same topo/cycle logic; cycle detection unchanged since rows share definition ids) and return
  an updated `RepeatingRows` under the group id. `FormRenderer.RecomputeCalculations` unchanged.
- **Cross-field validation:** a rule whose `Target` and every `Condition.Field` live inside the same
  group evaluates per row; a boundary-crossing rule is FR-04-blocked; outside-group rules unchanged.
- **Visibility:** `FilterToVisible` extends internally (signature unchanged) — per visible group,
  each row is filtered to its visible children via the row-scoped view with the same shrink-only
  fixed point; a hidden group's whole value drops. `GetVisibleNodes` **stops descending into a
  Repeating node's `Children`** (a deliberate, test-pinned behavior change); add
  `GetVisibleChildIds(repeatingNode, row, outerValues)` for the renderer/SubmissionView per-row need.

### Renderer
- `FieldValueConventions.GetStoredClrType(Repeating)` → `typeof(RepeatingRows)`; empty value = rows
  seeded to `MinRows ?? 0` (renderer seeds on `OnInitialized`).
- New internal `src/BlazeForms.Renderer/Components/RepeatingGroup.razor(.cs/.css)` (peer of
  `ErrorSummary`, **not** a `FormFieldBase`): gets the node, the `RepeatingRows` value, a per-row
  child-parameter builder + error lookup from `FormRenderer`, and `EventCallback`s for
  add/remove/move/child-change/child-blur. `FormRenderer.razor` branches on `Repeating` in the
  section loop, but first consults `IFieldComponentRegistry` — a host-registered Repeating gets the
  whole group as an ordinary `FormFieldBase` (`Value` = `RepeatingRows`, single group-level `Error`;
  documented limitation: per-child inline errors are the default component's affordance).
  `DefaultFieldComponents` adds no Repeating entry; its throw test is rewritten to assert File/Lookup
  still throw and Repeating is structural.
- **Markup:** outer `fieldset`/`legend` (group `Label`); each row a nested `fieldset` with `legend` =
  "{ItemLabel ?? Label} {n}". Children render through the existing `DynamicComponent` +
  `ResolveComponentType` (host per-field overrides keep working). `@key` = `rowId` on rows, `childId`
  within a row. Per-(child,row) DOM ids `{_instanceId}-{childId}-{rowId}`; per-(child,row) error keys
  use a separator outside `FormIds`' `[a-z0-9-]` alphabet so they can't collide with a node id.
- **Validation:** per-visible-group → per-row → per-visible-child `FieldValidator.Validate`
  (validator unchanged); group-level row-count rule vs `MinRows`/`MaxRows`; `PruneHiddenErrors` and
  `CrossFieldValidator` gain row-awareness. **Capture at submit:** nothing new — `FilterToVisible`
  + `ToJsonValues` produce the shape.
- **Draft resume:** `HydrateDraftValues`/`HydrateValue` recurse into each row and re-hydrate child
  values by child node type, reusing the `eab57cf` Date case per child.
- **`FormSubmissionView`:** `BuildNodeGroups` recurses — a group renders a sub-block per row (heading
  "{ItemLabel} n", then label/value rows), per-row visibility via `GetVisibleChildIds`.

**Keyboard + SR model (AGENTS invariant #4 — stated in the Increment B PR):** native
`<button type="button">` for Add (after rows) and per-row Remove / Move up / Move down (accessible
names carry the ordinal). Focus after each mutation — Add → first control of the new row; Remove →
next row's Remove (or previous, or the Add button if none remain); Move → stays on the pressed
button (travels with the row via `@key`). One visually-hidden `aria-live="polite"` region (pattern
of `_calcAnnouncement`) announces "'Dependent' 2 added. 3 of 5.", "…removed…", "…moved to position
1 of 3." At `MaxRows`/`MinRows`, the button stays rendered with `aria-disabled="true"` + no-op +
announcement (never native `disabled`, which ejects focus).

### Designer
- **Un-reserve (Increment C, not A):** `ReservedNodeTypesBacking = [File, Lookup]`; palette Advanced
  becomes `[Calc, NodeType.Repeating, ..ReservedNodeTypes]` explicitly; update `SerializationTests`
  (19 addable / 2 reserved) and `FieldPaletteTests`. `PhaseOneNodeTypes` keeps its name (public API);
  update its XML doc.
- **Canvas nesting — drill-in scope (recommended):** the repeating row is one ordinary `option` (type
  chip + "{n} fields" chip). Selecting it offers "Edit group fields"; the canvas re-scopes to the
  group's `Children` (breadcrumb + heading, `Esc`/back returns focus to the group's row). Every
  existing affordance (roving focus, reorder, duplicate, delete, lint markers, announcements) applies
  to children unchanged. (Rejected: inline nested rows — illegal in a `listbox`; `role="tree"`
  rewrite — too costly for one container.)
- **`DefinitionMutations`:** generalize location to a parent path that can end in a group's
  `Children`; insert/remove/update/move/duplicate operate within that list when scoped. **Guard: no
  repeating inside repeating** (mutation `ArgumentException` + palette hides Repeating while scoped).
  Cross-scope moves (child ↔ section) out this slice.
- **`DesignerEditContext`:** mutations carry the scope; undo/redo free (whole-definition mementos);
  selection snapshot records the scope.
- **`PropertiesPanel`:** repeating group — Label, Help, `ItemLabel`, `MinRows`, `MaxRows` (ints,
  `min=0`, cross-checked), "Edit group fields (n)"; `Required` hidden (use `MinRows ≥ 1`).
- **Delete protection:** deleting a group aggregates `ReferencesTo` over the group and every
  descendant and names them all; deleting a child works as today.
- **Rule editors:** field pickers become boundary-aware via a new
  `ExpressionDependencyAnalysis.GetRepeatingGroupOf(definition, nodeId)`; editing a child offers
  siblings + top-level fields, editing a top-level node excludes group children. Cycle detection
  unchanged.
- **Linter:** blocking **FR-04** (boundary violation), advisory **REP-01** (`MinRows > MaxRows`),
  advisory **REP-02** (group with no child fields). Each with violation + clean tests.

### Drafts & versioning
- `RepeatingRows` serializes into `FormDraft.Values` like any answer; rowIds persist so resume
  rebinds rows exactly. D13 unchanged (drafts pin to the starting version, so child shape can't drift
  mid-fill). A v2-pinned draft resumed by a v3 build contains no repeating values — nothing to do.

### Docs
- This plan checked in. PRD updates in the Increment A PR: §9 envelope example gains the repeating
  shape; §13 P2 table ticks `repeating`; decision log **D18** (answer model + rowIds + aggregation
  deferred). README/sample updated in Increment C.

## Phased plan — 3 PRs, dependency-ordered

### Increment A — Core: answer model, schema v3, row-scoped evaluation (shippable alone)
- **A1** Schema v3: `FormNode` + `MinRows`/`MaxRows`/`ItemLabel`; `FormSchema.CurrentVersion = 3`
  (Repeating stays reserved here); `FormIds.NewRowId`; PRD §9/§13/D18; Core `PublicAPI.Unshipped.txt`.
  Golden `form-definition-v3.json` (v1/v2 untouched, still read); round-trip + version-range tests.
- **A2** `RepeatingRows`/`RepeatingRow` + functional mutators; `FormValues` write case + strict read.
  `RepeatingRowsTests`, `FormValuesTests` (nested dates/choice lists; non-matching arrays stay
  opaque); pinned envelope JSON test.
- **A3** Row scoping: internal `RowScope` merge; `VisibilityEvaluator.FilterToVisible` per-row +
  hidden-group drop; `GetVisibleNodes` stops descending into repeating `Children`; new
  `GetVisibleChildIds`. `VisibilityEvaluatorTests` incl. the pinned behavior change.
- **A4** Per-row calc: `CalcEvaluator.EvaluateAll` repeating-aware (updated `RepeatingRows` under
  group id); `ExpressionDependencyAnalysis.GetRepeatingGroupOf`. `CalcEvaluatorTests` (per-row line
  totals, blank rows, calc-child cycles still null, non-repeating forms byte-identical).
- **A5** Linter: FR-04 (blocking), REP-01/REP-02 (advisory); pin `DanglingReferenceRule` coverage for
  refs inside `Children`. Violation + clean tests per rule; registered in `LintRuleIds`.

*Risk watch:* the `GetVisibleNodes` change is the one Core edit with existing consumers
(`FormRenderer.GetVisibleNodeIds`, `FormSubmissionView`); both treat children as flat today, already
wrong for repeating, and unreachable for P1 definitions (reserved type ⇒ no authored children). Pin
with a test regardless.

### Increment B — Renderer: fillable groups, capture, drafts, a11y (depends on A)
- `FieldValueConventions` (stored type + seeding); `RepeatingGroup` component; `FormRenderer`
  structural branch + registry-first check + per-(child,row) keying + live-region announcements +
  focus management; per-row validation + row-count rule + `PruneHiddenErrors`/`CrossFieldValidator`
  row-awareness + `ErrorSummary` anchoring; `HydrateDraftValues` row recursion; per-row calc display;
  `FormSubmissionView` recursion; rewrite the `DefaultFieldComponents` throw-pin test. bUnit
  `FormRendererRepeatingTests` (mutation → `RepeatingRows`, focus destinations, announcements,
  min/max no-op, render-count discipline, draft round-trip incl. `DateOnly`, envelope capture with
  hidden children/rows absent). PR states the full keyboard/SR acceptance criteria.

### Increment C — Designer authoring + sample + E2E (depends on B)
- Un-reserve + palette; child-aware `DefinitionMutations` + no-nested guard; canvas drill-in scope
  (breadcrumb, focus return, reorder parity inside scope); `PropertiesPanel` group; delete-protection
  descendant aggregation; boundary-aware rule-editor pickers; sample "Household members" group; E2E
  keyboard-only add/fill/remove/reorder + axe on fill and design.

## Risks & open questions
- **`GetVisibleNodes` no longer descends into repeating children** — a public-behavior change; note in
  release notes.
- **`aria-disabled` (not `disabled`) at min/max** — deliberate, to avoid focus ejection; cite in the
  PR so reviewers don't flag it.
- **Host-registered Repeating component** gets only `Value`/`ValueChanged`/single `Error` — document
  the seam's limits; the per-child override path inside rows keeps working.
- **Imported nested repeating** renders (structural branch recurses) but is not authorable — confirmed
  behavior, not a gap.
