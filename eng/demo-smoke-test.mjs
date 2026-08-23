// Boots the assembled WASM demo against a base URL that mimics the real deploy path
// (http://127.0.0.1:<port>/blaze-forms/demo/) and proves the one class of bug a bare
// `dotnet publish` can never catch: base-href-relative navigation actually working once the
// app isn't served from "/". Run via `npx --yes -p playwright node eng/demo-smoke-test.mjs
// <baseUrl>` -- see .github/workflows/ci.yml's demo-publish job.
//
// Usage: node eng/demo-smoke-test.mjs <baseUrl>

import { chromium } from 'playwright';
import { AxeBuilder } from '@axe-core/playwright';

const baseUrl = process.argv[2];
if (!baseUrl) {
  console.error('usage: node demo-smoke-test.mjs <baseUrl>');
  process.exit(1);
}

// The exact same five WCAG 2.2 AA tags BlazeForms.E2E.Tests/AccessibilityAssertions.cs's own
// Wcag22AaTags gates on (referenced by symbol name, not line number, since line numbers drift).
// This list is duplicated across the C#/Node boundary on purpose (abstracting two consumers of a
// five-element literal is not worth the indirection) -- if you change one, change the other.
const wcag22AaTags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

async function assertNoAxeViolations(page, scenario) {
  const results = await new AxeBuilder({ page }).withTags(wcag22AaTags).analyze();
  if (results.violations.length === 0) {
    console.log(`PASS: axe found zero WCAG 2.2 AA violations in "${scenario}"`);
    return;
  }

  const report = results.violations
    .map((v) => `[${v.impact}] ${v.id} — ${v.help} (${v.helpUrl})\n  targets: ${v.nodes.map((n) => n.target.join(', ')).join('; ')}`)
    .join('\n\n');
  throw new Error(`FAIL: axe found ${results.violations.length} WCAG 2.2 AA violation(s) in "${scenario}":\n${report}`);
}

const consoleErrors = [];
const browser = await chromium.launch();
// browser.newContext() explicitly, not the browser.newPage() shorthand: @axe-core/playwright's
// own AxeBuilder refuses to run against a page it can't trace back to a real context (see
// https://github.com/dequelabs/axe-core-npm/blob/develop/packages/playwright/error-handling.md).
const context = await browser.newContext();
const page = await context.newPage();
page.on('console', (msg) => {
  if (msg.type() === 'error') consoleErrors.push(msg.text());
});
page.on('pageerror', (err) => consoleErrors.push(String(err)));

function assert(condition, message) {
  if (!condition) {
    throw new Error(`FAIL: ${message}`);
  }
  console.log(`PASS: ${message}`);
}

try {
  // 1. Boots with zero console errors.
  await page.goto(baseUrl, { waitUntil: 'networkidle' });
  await page.waitForSelector('h1', { timeout: 30000 });
  assert((await page.textContent('h1')) === 'BlazeForms live demo', 'home page boots to its h1');
  await assertNoAxeViolations(page, '/ (demo home)');

  // 1a. The library and designer shell -- the two other demo pages the smoke flow below never
  //     otherwise visits. Both link back to home so this detour rejoins the original flow.
  await page.click('a[href="library"]');
  await page.waitForSelector('h1', { timeout: 30000 });
  assert((await page.textContent('h1')) === 'Form library', 'client-side nav to /library renders the form library');
  await assertNoAxeViolations(page, '/library');

  await page.click('a[href="./"]');
  await page.waitForSelector('h1', { timeout: 30000 });
  await page.click('a[href="design"]');
  await page.waitForSelector('.bf-designer', { timeout: 30000 });
  await assertNoAxeViolations(page, '/design');

  await page.click('a[href="./"]');
  await page.waitForSelector('h1', { timeout: 30000 });

  // 2. Client-side nav Home -> Fill works (base-href-relative <a href>, the blocker this test
  //    exists to catch: a root-absolute href here would bypass the router for a full page load).
  await page.click('a[href="fill"]');
  await page.waitForSelector('h1', { timeout: 30000 });
  assert((await page.textContent('h1')) === 'Benefits Enrollment', 'client-side nav to /fill renders the reference form');
  assert(consoleErrors.length === 0, 'zero console/page errors through boot + client nav');
  await assertNoAxeViolations(page, '/fill');

  // 2a. The high-contrast theme toggle (docs/accessibility-statement-plan.md, Increment C4) is a
  //     real toggle button, not decoration: aria-pressed is its ONE state signal (Blazor owns it
  //     declaratively, MainLayout.razor.cs), and the resulting [data-bf-theme="high-contrast"]
  //     state on <html> is itself an axe-clean scan -- the whole point of shipping the toggle is
  //     that this state is reachable by a click, so it is scanned exactly the way a reviewer
  //     would reach it, not just asserted against the CSS.
  await page.click('#demo-theme-toggle');
  assert((await page.getAttribute('#demo-theme-toggle', 'aria-pressed')) === 'true', 'high-contrast toggle reports aria-pressed="true" after one click');
  assert((await page.getAttribute('html', 'data-bf-theme')) === 'high-contrast', '<html> carries data-bf-theme="high-contrast" after one click');
  await assertNoAxeViolations(page, '/fill with the high-contrast theme toggled on');

  await page.click('#demo-theme-toggle');
  assert((await page.getAttribute('#demo-theme-toggle', 'aria-pressed')) === 'false', 'high-contrast toggle reports aria-pressed="false" after a second click');
  assert((await page.getAttribute('html', 'data-bf-theme')) === null, '<html> no longer carries data-bf-theme after toggling back off');

  // 3. Filling and submitting the three-page reference form reaches the Submission page.
  await page.getByLabel('Full legal name').fill('Jordan Rivera');
  await page.getByLabel('Email address').fill('jordan.rivera@example.com');
  await page.getByLabel('Date of birth').fill('1990-05-14');
  await page.getByLabel('No', { exact: true }).check();
  await page.getByRole('button', { name: 'Next' }).click();
  await page.getByRole('heading', { name: 'Coverage selection' }).waitFor();

  await page.getByLabel('Program type').selectOption({ label: 'Standard' });
  await page.getByLabel('Email', { exact: true }).check();
  await page.getByRole('button', { name: 'Next' }).click();
  await page.getByRole('heading', { name: 'Review and submit' }).waitFor();

  await page.getByLabel('Start date').fill('2026-01-01');
  await page.getByLabel('End date').fill('2026-12-31');
  await page.getByRole('button', { name: 'Submit' }).click();

  await page.waitForSelector('h1', { timeout: 30000 });
  assert((await page.textContent('h1')) === 'Submission received', 'submitting the form reaches the Submission page');
  // Submit navigates client-side straight to /submission/{id} -- this is that real page, not a
  // stand-in, so this scan is /submission/{id} itself, not a proxy for it.
  await assertNoAxeViolations(page, '/submission/{id}');

  // 4. Hard-refresh deep link: a full navigation (not client-side) straight to a client-routed
  //    path must still boot the shell -- this is exactly what the 404.html SPA fallback exists
  //    for, and exactly what a green `dotnet publish` cannot tell you either way.
  const deepLinkErrors = [];
  const deepLinkPage = await browser.newPage();
  deepLinkPage.on('console', (msg) => {
    if (msg.type() !== 'error') return;
    // The test harness's 404-fallback server (eng/serve-with-404-fallback.py) faithfully
    // reproduces GitHub Pages' own behavior of answering a deep link with the shell's markup at
    // HTTP 404 -- Chrome logs that top-level document response as a "failed to load resource"
    // console error regardless of body content. Real, not filtered: any error from the app
    // itself once it's running.
    if (msg.text().includes('the server responded with a status of 404')) return;
    deepLinkErrors.push(msg.text());
  });
  deepLinkPage.on('pageerror', (err) => deepLinkErrors.push(String(err)));
  await deepLinkPage.goto(`${baseUrl}fill`, { waitUntil: 'networkidle' });
  await deepLinkPage.waitForSelector('h1', { timeout: 30000 });
  assert((await deepLinkPage.textContent('h1')) === 'Benefits Enrollment', 'hard-refresh deep link to /fill loads the shell');
  if (deepLinkErrors.length > 0) console.error('deep-link console/page errors:', JSON.stringify(deepLinkErrors, null, 2));
  assert(deepLinkErrors.length === 0, 'zero console/page errors on the hard-refresh deep link');

  console.log('\nAll demo smoke test assertions passed.');
} finally {
  await browser.close();
}
