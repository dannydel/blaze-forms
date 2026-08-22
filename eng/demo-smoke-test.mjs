// Boots the assembled WASM demo against a base URL that mimics the real deploy path
// (http://127.0.0.1:<port>/blaze-forms/demo/) and proves the one class of bug a bare
// `dotnet publish` can never catch: base-href-relative navigation actually working once the
// app isn't served from "/". Run via `npx --yes -p playwright node eng/demo-smoke-test.mjs
// <baseUrl>` -- see .github/workflows/ci.yml's demo-publish job.
//
// Usage: node eng/demo-smoke-test.mjs <baseUrl>

import { chromium } from 'playwright';

const baseUrl = process.argv[2];
if (!baseUrl) {
  console.error('usage: node demo-smoke-test.mjs <baseUrl>');
  process.exit(1);
}

const consoleErrors = [];
const browser = await chromium.launch();
const page = await browser.newPage();
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

  // 2. Client-side nav Home -> Fill works (base-href-relative <a href>, the blocker this test
  //    exists to catch: a root-absolute href here would bypass the router for a full page load).
  await page.click('a[href="fill"]');
  await page.waitForSelector('h1', { timeout: 30000 });
  assert((await page.textContent('h1')) === 'Benefits Enrollment', 'client-side nav to /fill renders the reference form');
  assert(consoleErrors.length === 0, 'zero console/page errors through boot + client nav');

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
