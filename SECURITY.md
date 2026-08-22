# Security Policy

BlazeForms is a personal, MIT-licensed open source project maintained by [Daniel Del Grosso](https://github.com/dannydel). There is no dedicated security team — reports are handled on a best-effort basis, in the maintainer's spare time.

## Supported versions

Only the **latest published 0.x release** is supported with security fixes. BlazeForms is pre-1.0; there is no LTS branch and no backporting of fixes to older 0.x releases. Once 1.0 ships, this policy will be revisited.

| Version         | Supported          |
| ---------------- | ------------------ |
| Latest `0.x`      | ✅                  |
| Older `0.x`       | ❌                  |

## Reporting a vulnerability

**Do not open a public GitHub issue for a security vulnerability.**

Report privately via [GitHub Security Advisories](https://github.com/dannydel/blaze-forms/security/advisories/new). This keeps the report out of public view until a fix is available and lets you coordinate disclosure with the maintainer.

Include, where possible:

- A description of the issue and its impact.
- Steps to reproduce (a minimal `FormDefinition`/JSON repro is ideal).
- The BlazeForms package(s) and version(s) affected.

### Response expectations

This is a one-maintainer project, so there is no SLA. As a best-effort target:

- Initial acknowledgment: within a few days.
- Triage and a plan (fix, mitigation, or "not a vulnerability" with reasoning): within two weeks.

Fixes ship as a new patch/minor release per the [supported versions](#supported-versions) above; by the same principle as AGENTS.md invariant #3, nothing is ever mutated in a version already published.

## Security-relevant surfaces

Two areas of BlazeForms deliberately sit on the trust boundary between author/respondent content and rendered output or executable behavior. Anything that undermines the guarantees below is a vulnerability, not a bug report to file publicly:

1. **The safe-Markdown pipeline (Core).** Author-supplied Markdown-enabled strings (`help`, `paragraph`, `callout` — see [docs/PRD.md](docs/PRD.md) §5.1) render only through Core's shared [Markdig](https://github.com/xoofx/markdig) pipeline, with raw HTML disabled, images stripped, and link protocols allow-listed (AGENTS.md invariant #6). A way to make that pipeline emit raw HTML, script content, or a disallowed URL scheme into rendered output is a sanitization bypass and should be reported privately.
2. **`FormJson` deserialization of untrusted definition documents.** A `FormDefinition` JSON document may originate from an untrusted author or an untrusted import. Anything that turns deserializing such a document into unbounded resource consumption, unexpected type instantiation, or code execution — beyond the documented schema shape — is a vulnerability in `BlazeForms.Core`'s serialization layer, not expected behavior.

Vulnerabilities outside these two surfaces (e.g. in the Designer or Renderer UI, or in the sample app) are equally welcome via the same private channel.
