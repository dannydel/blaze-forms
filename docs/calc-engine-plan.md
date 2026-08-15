# Calc Evaluation Engine — Implementation Plan (P2)

> Status: **approved, in progress**. Turns the schema-only `calc` node type (PRD §5, §13; decision
> log D7) into a working read-only computed value. Honors D12 — one serializable expression tree,
> no string DSL.

## Ground truth (verified in code before planning)

- **Expression tree** (`src/BlazeForms.Core/Expressions/`): `ConditionGroup` / `Condition` /
  `ConditionOperator`, the pure static `ConditionEvaluator` (decimal coercion for numbers,
  `DateTimeOffset`/`DateOnly` for dates, ordinal text), `VisibilityEvaluator.FilterToVisible`
  (shrink-only fixed point), `ValidationRule`. The tree is strictly **boolean-valued**.
  `ExpressionDependencyAnalysis` **already exists on main** — `ReferencesTo` (with a `ReferenceKind`
  enum) and `WouldCreateCycle` (DFS, visibility graph). Reuse it; do not reinvent.
- **Schema**: `FormNode` has no calc property. `FormSchema.CurrentVersion = 1`.
  `FormSchema.IsInputNode(NodeType.Calc)` is `true` (pinned by `SerializationTests`).
  `FormJson.ValidateDeclaredSchemaVersion` already accepts `1..CurrentVersion`. Serialization is
  source-generated (`FormJsonContext`, trim-safe, camelCase, nulls omitted).
- **Renderer**: `FieldValueConventions.GetStoredClrType(NodeType.Calc)` returns `null`, the single
  gate that keeps `CalcField` from ever receiving `Value`/`ValueChanged`/`OnBlur`/`Error`.
  `CalcField` renders a read-only control showing `(Value as string) ?? Node.Placeholder ?? ""`.
  `FieldValidator` hard-skips `Calc`. `FormSubmissionView.BuildDisplay` special-cases calc.
- **Designer**: **Calc is already an enabled, addable palette entry** (only
  `FormSchema.IsReservedForLaterPhase` types are disabled; Calc is not reserved). No palette work
  needed. `VisibilityRuleEditor` is the reusable dialog pattern (focus trap, working-state +
  Apply, cycle rejection with `role=alert`). `DanglingReferenceRule` (FR-03) walks visibility +
  validation refs.

## Design decisions

### D-A. Expression shape — flat n-ary value expression, sibling of `ConditionGroup`

New types in `src/BlazeForms.Core/Expressions/` (namespace `BlazeForms.Expressions`, same record +
`JsonStringEnumConverter` conventions as `Condition`):

- `CalcExpression` — `{ "op": CalcOperation, "operands": CalcOperand[], "format": CalcFormat }`.
- `CalcOperation` (JSON names pinned): `sum`, `subtract`, `multiply`, `divide`, `dateAddDays`
  (date + numeric days → date), `dateDiffDays` (two dates → number). `subtract`/`divide` left-fold
  in operand order.
- `CalcOperand` — exactly one of `Field` (node id), `Number` (decimal literal), `Function`
  (`CalcFunction`, sole member `today`). "Exactly one" enforced in the evaluator/linter, **not** the
  constructor — untrusted deserialization must not throw beyond JSON conventions.
- `CalcFormat` — `number`, `integer`, `currency`, `date`. Display hint only; stored value is never
  rounded.
- `FormNode` gains optional `Calculation` (JSON `"calculation"`). `null` ⇒ exactly today's
  placeholder behavior.

Nesting (an operand carrying a nested expression) is a deliberate **additive** future growth path,
not P2.

### D-B. schemaVersion bump to 2 (one-time, Increment A)

`FormSchema.CurrentVersion → 2`. `ValidateDeclaredSchemaVersion` already accepts the range, so v1
documents keep loading with `Calculation = null` — no migration code. Keep `form-definition-v1.json`
and its test; **add** `form-definition-v2.json` with a representative calc `calculation`, plus
round-trip tests. The bump lands **once** in Increment A; later increments must not re-bump.

### D-C. Evaluation semantics — pure static `CalcEvaluator`

Styled like `ConditionEvaluator`:

- `object? Evaluate(CalcExpression, IReadOnlyDictionary<string, object?> values, DateOnly today)`
  → `decimal`, `DateOnly`, or `null`. **`today` is a parameter, never a clock read** — Core stays
  pure and deterministic.
- Coercion reuses `ConditionEvaluator`'s vocabulary; extract the shared `TryAsDecimal`/`TryAsDate`
  helpers into an internal `ValueCoercion` both evaluators use (deepen, don't fork).
- Null/blank/error policy (documented in XML docs):
  - `sum`: skip blank operands; all-blank ⇒ `null` (not `0`).
  - `subtract`/`multiply`/`divide`/date ops: any blank/non-coercible operand ⇒ `null`.
  - divide-by-zero ⇒ `null`, never a throw.
- `CalcEvaluator.EvaluateAll(FormDefinition, values, today)` → calc-node-id → result, in
  **topological order** over the calc dependency graph. Every member of an imported cycle ⇒ `null`.
  This is the renderer's single entry point.
- Author-time cycle detection extends `ExpressionDependencyAnalysis`: new
  `ReferenceKind.Calculation`; `ReferencesTo` also walks each node's `Calculation` operands; new
  `WouldCreateCalculationCycle(...)` reusing the existing DFS (refactored to take a prebuilt
  adjacency dict). Visibility and calc graphs stay **separate**.
- Calc values feed the boolean tree for free — the renderer writes computed values into the answer
  dict, so `visibleWhen` / cross-field validation can reference calc fields with no evaluator change.

### D-D. Capture-at-submit, not recompute-at-view

Computed value is written into `FormRenderer._values` under the calc node id, flows through
`FilterToVisible` into the envelope as a JSON number/date-string. Submissions stay immutable
(success criterion #5). `FormSubmissionView` reads the captured value; falls back to the placeholder
only for pre-engine / v1 envelopes. Drafts persist it harmlessly (overwritten on resume).

### D-E. Renderer reactivity + display

- One private `FormRenderer.RecomputeCalculations()` calling `CalcEvaluator.EvaluateAll`, invoked
  from `SetValue` (after the write), end of `LoadDraftAsync`, and once in `OnInitialized`.
- Time source: optional `TimeProvider` from `ServiceProvider` (same optional-service pattern as
  the sink/draft store), default `TimeProvider.System`;
  `today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime)`.
- `GetStoredClrType(Calc)` **stays `null`**; add one explicit calc branch in `BuildFieldParameters`
  passing `Value` = the formatted display string. `CalcField` already treats a string `Value` as
  read-only display text.
- **A11y (approved):** `CalcField` control becomes a semantic `<output for=…>` (implicit
  `role=status` live region), keeping the `<label for>` association and token-styled read-only look.
  Ships with keyboard/SR acceptance criteria in the PR (AGENTS invariant #4).
- `FormSubmissionView.BuildDisplay` calc branch: captured value present → format per
  `node.Calculation?.Format`; absent → placeholder fallback.

### D-F. Designer

- Palette: no change (Calc already addable).
- `PropertiesPanel`: calc-only group (read-only summary via new `CalculationSummaryFormatter`,
  Add/Edit/Remove buttons), mirroring the visibility-rule trio incl. focus return.
- New `Rules/CalculationEditor.razor(.cs/.css/.js)` following `VisibilityRuleEditor` exactly:
  focus-trapped, working-state, one commit on Apply, Esc-cancel, cycle rejection via
  `WouldCreateCalculationCycle` in a focused `role=alert`. Operand rows via new `CalcOperandRow`
  (field/number/today toggle, field select filtered by operand typing). `ConditionRow` is **not**
  reused (it edits booleans) — reuse its labelling/focus *patterns* only.
- `DeleteProtectionDialog.Describe` gains a `ReferenceKind.Calculation` case.
- Linter: extend `DanglingReferenceRule` (FR-03) to walk `Calculation` operand field refs (same
  rule/ID). Add advisory `CALC-01` ("calc field has no calculation") — cheap, registry-friendly.
- Preview: free (`PreviewPane` hosts `FormRenderer` with `Ephemeral`).
- Localization: new `DesignerStrings`/`RendererStrings` resx entries for all new labels.

## Phased plan — 3 PRs, dependency-ordered

### Increment A — Core: schema + engine (shippable alone)
- **A1** schema types + serialization: new `CalcExpression`/`CalcOperation`/`CalcOperand`/
  `CalcFormat`/`CalcFunction`; edit `FormNode` (`Calculation`), `FormSchema` (`CurrentVersion=2`),
  `FormJsonContext`, optional `FormJson` helpers, Core `PublicAPI.Unshipped.txt`. Tests first:
  `SerializationTests` (enum JSON names pinned), round-trip, `GoldenFileTests` + new
  `form-definition-v2.json`, v1-still-reads, `TestDefinitions` calc-with-expression.
- **A2** evaluator: new `CalcEvaluator`; refactor shared coercion into internal `ValueCoercion`
  (`ConditionEvaluatorTests` stay green untouched = the refactor's regression harness). New
  `CalcEvaluatorTests`.
- **A3** dependency analysis + linter: extend `ExpressionDependencyAnalysis`
  (`ReferenceKind.Calculation`, walk calculations, `WouldCreateCalculationCycle`, DFS refactor);
  `DanglingReferenceRule`; new `CalcMissingExpressionRule` + `LintRuleIds`. Tests for each.

### Increment B — Renderer: reactivity + capture (depends on A)
- `FormRenderer` wiring (`RecomputeCalculations`, `TimeProvider`, calc branch in
  `BuildFieldParameters`), `CalcField` `<output>`, `FormSubmissionView` branch, shared
  `CalcDisplayFormatter`. Tests: `CalcFieldTests`, rewrite `FormRendererStructureTests` calc pin as
  "receives Value but never ValueChanged/OnBlur/Error", new `FormRendererCalcTests` (reactivity,
  envelope capture, render-count, draft resume, deterministic `today()`), `FormSubmissionViewTests`,
  `FillMudSmokeTests` (success criterion #4 holds).

### Increment C — Designer authoring + sample + E2E (depends on B)
- New `CalculationEditor` + `CalcOperandRow` + `CalculationSummaryFormatter`; `PropertiesPanel` calc
  group; `DeleteProtectionDialog` case; resx; Designer `PublicAPI.Unshipped.txt`. Sample enrollment
  form gets a real calc; E2E `FillAccessibilityTests` + `DesignAccessibilityTests` (keyboard-only
  authoring, axe). Each interactive component ships keyboard/SR acceptance criteria in its PR.

## Resolved decisions (recommended choices — approved)

1. **Currency formatting** — `currency` format = `CultureInfo.CurrentCulture` two-decimal numeric
   with **no symbol** (matches the bare `CurrencyField`); invariant for storage. A currency-code
   property is a future additive schema change.
2. **Decimal precision** — store full `decimal`; round only at display. `integer` ⇒
   `MidpointRounding.AwayFromZero`. Never round-trip a rounded value into the envelope.
3. **`today()` timezone** — `TimeProvider.GetLocalNow()` = server-local date on Blazor Server;
   **accepted for P2 and documented**. Client-timezone resolution (JS interop) is out of scope.
4. **`CalcField` markup** — semantic `<output>` with `role=status` (approved; ships with a11y
   acceptance criteria).
5. **`FormRendererStructureTests` contract** — the "calc never receives Value" pin is
   **deliberately inverted** to "receives Value, never ValueChanged/OnBlur/Error". Intended P2
   behavior change, not a regression.

## Risks to watch

- **Cross-graph visibility↔calc**: a calc feeding a visibility rule that hides the calc's own input
  can oscillate in theory; `FilterToVisible`'s shrink-only fixed point bounds it. Pin observed
  behavior with one adversarial test (calc → visibility → input chain).
- **schemaVersion=2 is a public-contract event** — land it once (Increment A); anything else wanting
  a schema change rides the same bump.
