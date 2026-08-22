# Adoption Infrastructure — Implementation Plan

> Status: **proposed**. Makes BlazeForms installable (the README's `dotnet add package` currently
> promises something the repo cannot deliver), demonstrable (hosted WASM demo), machine-consumable
> (published JSON Schema for the definition format), and legible to drive-by contributors (OSS
> hygiene + CI hardening). No product or schema change: `schemaVersion` stays 3; the only public
> API addition is one Core type in Increment C. Modeled on `docs/repeating-groups-plan.md`
> (dependency-ordered increments, each independently shippable). Honors AGENTS invariants #2
> (schema is a contract — the exported JSON Schema is golden-pinned like the wire format) and #3
> (published versions immutable — schema files are add-only, never edited).

## Resolved decisions

1. **WASM demo shape — new standalone `samples/BlazeForms.Demo.Wasm`.** Converting the existing
   sample to `InteractiveAuto` would restructure the app that eight E2E a11y suites launch
   (`SampleAppFixture.cs`) — putting the axe gate at risk to serve a demo. The demo instead
   duplicates five thin pages and shares only the data seed via a linked compile item
   (`EnrollmentForm.cs`), so the reference form can never drift. MudBlazor is excluded from the
   demo (payload; it is the sample's registry honesty test, not a demo feature). A shared
   `Demo.Shared` RCL was considered and deferred — it moves routable components out of the
   E2E-covered assembly.
2. **Versioning — MinVer, tag as single source of truth.** `MinVerTagPrefix=v`; the static
   `<Version>` in `src/Directory.Build.props` is deleted. Rejected: Nerdbank.GitVersioning (more
   machinery than a one-maintainer repo needs) and keep-static-plus-gate (the failure mode is a
   forgotten bump discovered only when NuGet rejects the duplicate). Requires `fetch-depth: 0` in
   every job that packs.
3. **JSON Schema — exporter-generated inside Core, golden-pinned.** New public
   `FormJsonSchema.CreateDefinitionSchema()` uses `JsonSchemaExporter` over `FormJson.Options`
   (verified: all seven serializable roots export cleanly; `JsonStringEnumMemberName` values come
   through; `FormNode.Children` recursion emits as a JSON-pointer `$ref`; the exporter is
   trim/AOT-clean under source-gen and not `[Experimental]`). Hand-authoring a ~375-line schema
   tracking `FormNode`'s 25+ properties would be a second source of truth that silently rots.
   Drift fails CI in the same place a wire-format drift does.
4. **NuGet publish trigger — tag push `v*`** plus `workflow_dispatch` for retries. `NUGET_API_KEY`
   lives in a GitHub Environment named `nuget` (protection rules attachable later without
   reworking the workflow). Both `.nupkg` and `.snupkg` are pushed explicitly with
   `--skip-duplicate`; the same job creates the GitHub Release and attaches packages + schema.
   Rejected: Release-triggered publishing (decouples tag from artifact, adds a UI step).
5. **Coverage — in-repo only, no external service.** `coverlet.collector` is already referenced by
   the four unit-test projects; add collection to the existing `dotnet test`, merge with
   `reportgenerator` into a `$GITHUB_STEP_SUMMARY` Markdown summary, upload Cobertura XML as an
   artifact. Codecov revisited only if a badge is wanted.
6. **PublicAPI gate — do not require empty `Unshipped` before 1.0.** The Unshipped files hold ~1,170
   lines, including API-hygiene debt (`_Imports`, `Canvas.CanvasNodeRow`, `PageTabStrip`, public
   `OnAfterRenderAsync` overrides) that must not be frozen as shipped v1 API.
   `TreatWarningsAsErrors` already makes RS0016/RS0017 hard errors, so the files can never be
   stale — that is the real protection. This slice adds a parse/sort CI check and a documented
   `eng/mark-api-shipped.sh` run manually at 1.0 (release workflow enforces it only for tags with
   no prerelease label).
7. **Pages host — project subpath** (`https://<owner>.github.io/blaze-forms/`; demo at `/demo/`,
   schemas at `/schemas/`). Deploy via `configure-pages` → `upload-pages-artifact` →
   `deploy-pages` in a separate `pages.yml` so a Pages failure never gates a PR. Base href is
   rewritten post-publish with a grep guard; `.nojekyll` at the artifact root (or Jekyll drops
   `_framework/`); `404.html` is a byte copy of `index.html` for SPA deep links.

## Ground truth (verified in code and by probe before planning)

- **Packing works today, badly.** `dotnet pack src/BlazeForms.Core -c Release` succeeds with zero
  warnings; the nuspec shows SourceLink already active via the SDK's implicit
  `Microsoft.SourceLink.GitHub` (repo URL + commit stamped) — no package reference needed. But it
  ships `<description>Package Description</description>` (the MSBuild placeholder), no tags, no
  readme, no icon, no `PackageProjectUrl`, and no symbols package. Metadata lives only in
  `src/Directory.Build.props` (Authors, MIT, RepositoryUrl, static `Version=0.1.0-preview.1`,
  `GenerateDocumentationFile`, PublicApiAnalyzers 5.6.0).
- **`samples/BlazeForms.Sample.csproj` lacks `IsPackable=false`** — a solution-wide pack would try
  to pack the web app. All five test projects set it.
- **PublicAPI state:** `PublicAPI.Shipped.txt` is one line (`#nullable enable`) in all three
  packages; `Unshipped` = Core 584 / Designer 375 / Renderer 207 lines. See decision 6.
- **CI:** `.github/workflows/ci.yml` is the only file under `.github/` — `build-and-test` and
  `e2e-a11y` jobs, `push`/`pull_request` on main, `fetch-depth` default (shallow — breaks MinVer
  unless overridden). No dependabot, CodeQL, coverage, templates, SECURITY, CoC, or CHANGELOG.
  `.gitignore` already covers `*.nupkg`, `*.snupkg`, `coverage*`, `artifacts/`.
- **JSON Schema probe (ran against the real Core):**
  `FormJson.Options.GetJsonSchemaAsNode(typeof(FormDefinition))` works for all seven roots. With
  `TreatNullObliviousAsNonNullable = true` the nullability noise disappears. Full definition
  schema ≈ 375 pretty-printed lines. No `JsonPolymorphic`/custom converters anywhere — nothing the
  exporter can't model. Quirks accepted and documented: pointer `$ref`s instead of `$defs`;
  envelope `values` exports as unconstrained `{"type":"object"}`; per-`NodeType` conditional
  requirements are inexpressible without `allOf`/`if-then` — that remains `FormLinter`'s job.
  Golden mechanism to reuse: `GoldenFileTests.cs` + `BLAZEFORMS_UPDATE_GOLDEN=1`.
- **WASM probe (actually published):** a throwaway standalone `blazorwasm` app referencing
  Renderer + Designer publishes successfully; every `_content/BlazeForms.*` asset lands;
  `Markdig.wasm` survives trimming; ≈4.4 MB Brotli payload. Re-publishing with
  `-p:SuppressTrimAnalysisWarnings=false` surfaces exactly two real findings: IL2110/IL2111 on
  `DynamicComponent.Type` at `FormRenderer.razor:52` and `Components/RepeatingGroup.razor:2` (the
  `IFieldComponentRegistry` seam). Core and Designer are clean — Core's `IsTrimmable` claim holds.
  Both RCLs already declare `<SupportedPlatform Include="browser" />`; all 10 collocated
  `.razor.js` modules are module-import interop with no server-only dependency.
- **What pins the demo shape:** every sample page hardcodes `@rendermode InteractiveServer`;
  `App.razor` uses Blazor-Web-only host features (`@Assets`, `<ImportMap />`, `ReconnectModal`);
  `SampleAppFixture.cs:40-84` launches the sample by path via `dotnet run` for eight E2E suites.
  The sample's in-memory store registrations and seeding (`Program.cs:27-32`) port to WASM
  verbatim (a WASM singleton is per-tab — exactly right for a demo).
- Localization is invariant-only (two resx files, no satellite cultures to preserve in publish).

## Phased plan — 4 increments, dependency-ordered

### Increment A — Packaging, versioning, release workflow (no dependencies)

- **A1** Package metadata in `src/Directory.Build.props`: `PackageProjectUrl`, `PackageTags`
  (`blazor;forms;form-builder;accessibility;wcag;json;dynamic-forms`), `PackageReadmeFile`,
  `PackageIcon`, `IncludeSymbols` + `SymbolPackageFormat=snupkg`, `PublishRepositoryUrl`,
  `EmbedUntrackedSources`, `PackageReleaseNotes` → CHANGELOG; pack the README and `assets/icon.png`
  (new asset — see open questions). Per-project `<Description>` in each `src/*.csproj` (the
  placeholder ships today).
- **A2** `samples/BlazeForms.Sample.csproj` gains `IsPackable=false`.
- **A3** MinVer in `src/Directory.Build.props` (static `<Version>` removed);
  `ContinuousIntegrationBuild` when `GITHUB_ACTIONS` in the root `Directory.Build.props`.
- **A4** `CHANGELOG.md` (Keep-a-Changelog; backfill calc-engine and repeating-groups entries).
- **A5** `.github/workflows/release.yml`: tag `v*` + `workflow_dispatch`; `fetch-depth: 0`; build →
  test → `dotnet pack` the three `src/` projects → upload; then a `publish` job with
  `environment: nuget` pushing `.nupkg` + `.snupkg` (`--skip-duplicate`) and `gh release create`.
  Assert packed version == tag before pushing (MinVer + shallow clone fails silently otherwise).
- **A6** `ci.yml` gains a pack step (+ `fetch-depth: 0` on that job) so packability is proven per
  PR, not first discovered at tag time. README gains NuGet badges.
- **Verify:** inspect the packed nuspec for real description/tags/readme/icon + `.snupkg`
  presence; `git tag v0.2.0-preview.1` → MinVer resolves it in build output; `workflow_dispatch`
  dry-run of release.yml with the push step disabled.

### Increment B — OSS hygiene + CI hardening (independent of A; can land in parallel)

- **B1** `SECURITY.md`: supported = latest 0.x; private reporting via GitHub Security Advisories;
  names the two security-relevant seams — safe-Markdown (AGENTS invariant #6) and untrusted
  definition JSON (`FormJson.cs`).
- **B2** `CODE_OF_CONDUCT.md` (Contributor Covenant 2.1).
- **B3** `.github/ISSUE_TEMPLATE/{bug_report,feature_request}.yml` + `config.yml`;
  `PULL_REQUEST_TEMPLATE.md` embedding the a11y acceptance-criteria block AGENTS.md already
  requires in prose (tab/arrow model, focus destination, live-region text, axe result), a DCO
  checkbox, and a "PublicAPI.Unshipped.txt updated" checkbox.
- **B4** `.github/dependabot.yml`: `nuget` weekly (grouped: `Microsoft.*` / test deps; MudBlazor
  and Markdig separate) + `github-actions` weekly.
- **B5** `.github/workflows/codeql.yml`: csharp, main + weekly cron, explicit build.
- **B6** Coverage in `ci.yml` per resolved decision 5.
- **B7** `CONTRIBUTING.md` expansion: build/test/E2E commands, `BLAZEFORMS_UPDATE_GOLDEN=1`
  workflow, PublicAPI expectations, DCO. Optional `CODEOWNERS`.
- **Verify:** throwaway PR shows CodeQL + coverage summary + rendered templates; dependabot config
  validates in Insights.

### Increment C — JSON Schema artifact + docs (Core code independent; release attachment depends on A)

- **C1** `src/BlazeForms.Core/Serialization/FormJsonSchema.cs`: public static
  `CreateDefinitionSchema()` returning `string` (keeps `JsonNode` off the public surface);
  exporter + `TransformSchemaNode` stamping `$schema` (draft 2020-12), `$id`, `title`, and
  constraining `schemaVersion` to `1..FormSchema.CurrentVersion`. PublicAPI.Unshipped updated in
  the same commit.
- **C2** `tests/BlazeForms.Core.Tests/SchemaExportTests.cs`: golden comparison against
  `Golden/form-definition-v3.schema.json`; a test asserting every `NodeType` /
  `ConditionOperator` / `CalcOperation` / `CalcFunction` / `CalcFormat` / `ConditionJoin` JSON
  name appears in the exported enum arrays (catches an enum added without a schema regen); a test
  pinning the `schemaVersion` bounds to `FormSchema.CurrentVersion`.
- **C3** `docs/schemas/form-definition-v3.schema.json` — identical bytes to the golden file, with
  a test asserting they match. Add-only: a future `schemaVersion` bump adds `-v4`, never edits.
- **C4** `docs/schema.md`: consumption (VS Code `json.schemas`, `$schema` in a definition file),
  the `$id`/versioning policy, and the explicit list of constraints the schema cannot express
  (per-type requirements → linter).
- **C5** `release.yml` attaches `docs/schemas/*.schema.json`; minimal `.github/workflows/pages.yml`
  publishes `docs/schemas/**` + a landing page so the `$id` URL is live before the demo exists
  (de-risks the base-path decision early). README links the schema doc.
- **Verify:** unit tests; validate the golden definition against the schema with an external
  validator (`npx ajv-cli validate --spec=draft2020 …`) — catches output that is valid JSON but
  not a valid *schema*; `$id` URL returns 200 after the Pages run.

### Increment D — WASM demo + Pages deploy (depends on C's pages.yml; independent of A/B)

- **D1** `samples/BlazeForms.Demo.Wasm/`: `Microsoft.NET.Sdk.BlazorWebAssembly`,
  `IsPackable=false`, references Renderer + Designer, linked `EnrollmentForm.cs`,
  `<TrimmerRootAssembly>` for both RCLs (mitigates the measured IL2110/IL2111 on the registry
  seam), `SuppressTrimAnalysisWarnings=false` so regressions surface, globalization left on.
- **D2** `Program.cs` mirrors the sample's in-memory registrations + seeding; a visible
  "in-browser memory only — refresh loses it" banner.
- **D3** Plain static `wwwroot/index.html` (`<base href="/" />` rewritten at deploy, the two RCL
  stylesheets, `blazor.webassembly.js`); `App.razor`/layout/5 pages copied from the sample with
  `@rendermode` removed and MudBlazor dropped. Project added to `BlazeForms.sln`.
- **D4** `ci.yml` gains a `demo-publish` job (`dotnet publish` on PRs) so trim/publish breakage
  surfaces at review time.
- **D5** `pages.yml` extended: demo into `_site/demo/`, base-href rewrite + grep guard,
  `404.html` copy, `.nojekyll`, schemas preserved. README + docs link the live demo.
- **Verify:** local publish + static serve; click through fill → submit → submission view and the
  designer's JS-module-heavy paths (drag, undo, publish dialog, keyboard help); zero IL2xxx with
  suppression off; deployed site loads in a fresh profile with no 404s under `_content/` or
  `_framework/`.

## Risks & open questions

**Need a decision before/at Increment A**

- **Publish timing + NuGet ID availability.** Recommendation: land A, tag `v0.2.0-preview.1`, and
  publish immediately to reserve the three `BlazeForms.*` IDs. PRD D10's "name clear" check is
  from 2026-08-10 — **re-verify availability before A merges**; a squatted ID invalidates the
  packaging design.
- **PublicAPI gate policy** — accept resolved decision 6 (defer shipped-freeze to 1.0), or enforce
  empty-Unshipped now and pay the API-cleanup bill inside this slice.
- **`assets/icon.png`** — needs an actual image; MIT-compatible original artwork only (never Tyler
  assets, per standing constraint).
- **Package README links** — the root README's repo-relative links break on nuget.org.
  Recommendation: absolutize them in the shipped copy.
- **Pages host** — subpath now; a later custom-domain move changes the base-href rewrite *and* the
  schema `$id` (a contract → new file). Prefer getting the `$id` host right once, up front.

**Technical**

- **Registry-seam trimming (measured).** `TrimmerRootAssembly` fixes the demo but not downstream
  WASM hosts registering custom components via `IFieldComponentRegistry` — their parameter setters
  may be trimmed. Proper fix is `[DynamicallyAccessedMembers]` on the `Type` flowing through the
  registry — a public API change, out of scope; file a follow-up issue and document the
  `TrimmerRootAssembly` / `PublishTrimmed=false` workaround. Demo fallback if rooting proves
  insufficient: `PublishTrimmed=false` (~2× payload).
- **First-load size on Pages.** ≈4.4 MB Brotli, but GitHub Pages does not content-negotiate `.br`.
  Measure real transfer after deploy; mitigations in order: accept → service worker → drop the
  Designer from the demo. Do not silently ship a 15 MB first load.
- **Exporter output stability across SDK patches.** The golden test is the tripwire; an SDK bump
  may redden CI unrelated to the PR. `docs/schema.md` documents the regen-and-read-the-diff
  workflow; a changed schema without a `schemaVersion` bump is a finding, not a formality.
- **MinVer + shallow clones** silently yield `0.0.0-alpha.0.x` — hence the version==tag assertion
  in release.yml and `fetch-depth: 0` on packing jobs.
- **CodeQL vs `TreatWarningsAsErrors`** — if the CodeQL build flakes on analyzer diagnostics, relax
  warnings-as-errors for that job only; the real gate stays in ci.yml.
- **Sample/demo drift.** Only the data seed is shared; the five demo pages are copies. Accepted —
  the per-PR demo publish means an API change breaks the demo build immediately.
- **API-hygiene debt made visible** (`_Imports`, `Canvas.*`, public `OnAfterRenderAsync` overrides
  in Unshipped). A 1.0 blocker, not an adoption task — file the issue now.
