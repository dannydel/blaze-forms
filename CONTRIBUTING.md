# Contributing to BlazeForms

Thanks for considering a contribution!

- **How we build** — every standard (naming, tests, accessibility gates, commit format) is in [AGENTS.md](AGENTS.md). It is the canonical, always-current source of truth for contribution standards — this file just gets you set up to follow it. It is written for humans and coding agents alike; read it before your first PR.
- **What we're building and why** — scope, phasing, and locked decisions are in [docs/PRD.md](docs/PRD.md). Check the decision log before proposing a direction change.
- **Bugs** — open an issue with a minimal repro; a PR that starts with a failing regression test is the fastest path to a merge.

## Prerequisites

- .NET SDK matching [`global.json`](global.json) (currently `10.0.302`, `rollForward: latestFeature`). Installing a matching or newer feature-band SDK is enough; the SDK resolver handles the rest.
- No other tooling is required to build and run the unit/component test suites. The end-to-end accessibility suite additionally needs Playwright's bundled Chromium (see below) and `pwsh` (PowerShell), which `playwright.ps1` requires and which does not ship with the .NET SDK — install it with `dotnet tool install --global PowerShell` if it isn't already on your machine.

## Build, test, and E2E commands

```bash
# Build everything
dotnet build BlazeForms.sln -c Release

# Unit + component tests (Core xUnit, Renderer/Designer bUnit, architecture tests)
dotnet test BlazeForms.sln -c Release

# End-to-end designer/renderer flows + the Playwright + axe accessibility gate.
# Deliberately outside BlazeForms.sln (PRD §11) so a plain solution build/test
# stays browser-free.
dotnet test tests/BlazeForms.E2E.Tests/BlazeForms.E2E.Tests.csproj
```

The E2E project launches the sample host with Playwright and scans it with axe. If Chromium
is not already installed locally, build the project once and run its generated installer:

```bash
dotnet build tests/BlazeForms.E2E.Tests/BlazeForms.E2E.Tests.csproj
pwsh tests/BlazeForms.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

## Golden-file tests

Serialization golden files pin the exact JSON shape of representative definitions per
`schemaVersion` (AGENTS.md invariant #2). When you intentionally change what a definition
serializes to — e.g. adding a new optional property — regenerate the golden files instead of
hand-editing them:

```bash
BLAZEFORMS_UPDATE_GOLDEN=1 dotnet test tests/BlazeForms.Core.Tests
```

Review the resulting diff carefully: a changed golden file is a schema-shape change and must
come with a `schemaVersion` bump, round-trip tests, and a `CHANGELOG.md` entry, by the same
principle as invariant #2. An unexpected diff (one you didn't intend) is a regression, not
something to regenerate away.

The same `BLAZEFORMS_UPDATE_GOLDEN=1` run also regenerates the exported JSON Schema golden file
and its `docs/schemas` copy together, so the two never desync — see
[docs/schema.md](docs/schema.md) for the schema-specific publish policy (add-only once a version
ships) before regenerating it.

## Public API changes

Public surface changes to `BlazeForms.Core`, `BlazeForms.Renderer`, and `BlazeForms.Designer`
are enforced by `Microsoft.CodeAnalysis.PublicApiAnalyzers`, with `TreatWarningsAsErrors` on:

- Adding a public member without adding its line to that project's `PublicAPI.Unshipped.txt`
  fails the build with **RS0016** (unshipped API not documented).
- Removing or changing a public member without updating `PublicAPI.Unshipped.txt`/
  `PublicAPI.Shipped.txt` accordingly fails with **RS0017** (unused/stale API entry).

Add or update the relevant `PublicAPI.Unshipped.txt` lines **in the same commit** as the API
change — most IDEs and `dotnet build` surface a code fix that generates the correct line. Do not
hand-move entries into `PublicAPI.Shipped.txt` yourself; the maintainer promotes
Unshipped → Shipped entries at a release boundary, not as part of a regular contribution.

## Commits

- **Conventional Commits** — `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`, etc. PR
  titles follow the same convention.
- **DCO sign-off** — this project uses the [Developer Certificate of Origin](https://developercertificate.org/).
  Add `Signed-off-by` to every commit (`git commit -s`). This is required by convention and
  checked in review, not by an automated gate — but a PR with unsigned commits will be asked to
  amend before merge.

## Immutability of published artifacts

By the same principle as invariant #3 (nothing may mutate a published definition version), a
`CHANGELOG.md` entry under a version heading that has already been released is never edited —
a correction lands as a new entry, not a rewrite of the old one. The same applies to any
already-tagged release's packages.
