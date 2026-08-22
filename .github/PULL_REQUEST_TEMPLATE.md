## Summary

<!-- What does this PR do, and why? Link the issue it closes, if any. -->

## Checklist

- [ ] Commits are signed off per the [DCO](https://developercertificate.org/) (`git commit -s`)
- [ ] PR title follows [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`, …)
- [ ] Tests added or updated for the change (xUnit for Core, bUnit for components, Playwright + axe for E2E a11y flows)
- [ ] `PublicAPI.Unshipped.txt` updated in the same commit, if this changes a public API
- [ ] `CHANGELOG.md` updated, if this is a user-visible change
- [ ] If the definition JSON shape changed: `schemaVersion` bumped, golden files regenerated, round-trip tests updated (AGENTS.md invariant #2)
- [ ] Docs and the sample app updated in the same PR (AGENTS.md workflow #3)

## Accessibility acceptance criteria

**Required for any UI change** (AGENTS.md invariant #4 — WCAG 2.2 AA, keyboard parity with pointer operations). Fill this in for every new or modified interactive component; if this PR has no UI change, write "N/A — no UI change."

- **Tab / arrow-key model**: <!-- What is focusable via Tab, in what order? Do arrow keys move within a composite widget (e.g. a listbox, toolbar, grid)? Describe the full keyboard interaction model, not just "it's focusable." -->
- **Focus destination after mutation**: <!-- After each mutating action (add, delete, reorder, submit, dialog open/close, etc.), where does focus land? -->
- **Live region announcement**: <!-- What text, if any, is announced via aria-live (or an equivalent) as a result of this change? Quote the exact announced string(s). -->
- **Axe scan result**: <!-- Result of the Playwright + axe accessibility scan for the affected page(s)/component(s) — e.g. "0 violations" with a link to the CI run, or paste the relevant output. -->
