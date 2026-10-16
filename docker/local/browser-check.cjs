// Real UI acceptance against the disposable local lab. Credentials are never printed.
const { chromium } = require(process.env.INDEXTOOLS_PLAYWRIGHT_MODULE || 'playwright');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { execFileSync } = require('node:child_process');
const root = path.resolve(__dirname, '../..');
const environment = Object.fromEntries(fs.readFileSync(path.join(root, '.private/local/compose.env'), 'utf8').split(/\r?\n/).filter(line => line.includes('=') && !line.trimStart().startsWith('#')).map(line => [line.slice(0, line.indexOf('=')), line.slice(line.indexOf('=') + 1)]));
const base = 'http://127.0.0.1:' + (process.env.LAB_TOOLS_PORT || environment.LAB_TOOLS_PORT || 5189);
const compose = ['compose', '-p', 'index-tools-lab', '--env-file', path.join(root, '.private/local/compose.env'), '-f', path.join(root, 'docker/local/compose.yaml')];

(async () => {
  const browser = await chromium.launch({ headless: true, ...(process.env.INDEXTOOLS_BROWSER_CHANNEL ? { channel: process.env.INDEXTOOLS_BROWSER_CHANNEL } : {}) });
  try {
    const context = await browser.newContext({ viewport: { width: 1440, height: 1100 } });
    const token = fs.readFileSync(path.join(root, '.private/local/tools/web-token'), 'utf8').trim();
    const login = await context.request.post(base + '/login', { form: { token } });
    assert.ok(login.ok());
    const page = await context.newPage();
    const navigate = async url => {
      let connected;
      const circuit = new Promise(resolve => connected = resolve);
      page.on('websocket', socket => socket.on('framesent', event => { if (Buffer.from(event.payload).includes(Buffer.from('OnRenderCompleted'))) connected(); }));
      await page.goto(url, { waitUntil: 'domcontentloaded' });
      await Promise.race([circuit, new Promise((_, reject) => setTimeout(() => reject(new Error('Blazor circuit not ready')), 20000))]);
    };
    await navigate(base + '/operations');
    assert.equal(await page.locator('#operator-source').inputValue(), 'live');
    await page.getByRole('button', { name: 'Read LastActivityId', exact: true }).click();
    await page.getByTestId('activity-result').waitFor();
    console.log('LIVE_ACTIVITY_READ_PASS');
    assert.equal(await page.getByRole('button', { name: 'Create working copy', exact: true }).isDisabled(), true);
    await page.getByRole('button', { name: 'Validate index', exact: true }).click();
    await page.getByTestId('validation-result').waitFor({ timeout: 90000 });
    const validationText = await page.getByTestId('validation-result').innerText();
    console.log(JSON.stringify({ ValidationText: validationText }));
    assert.match(validationText, /Validation completed successfully/);
    console.log('INDEX_VALIDATION_PASS');
    await page.locator('#backup-folder').fill('browser-' + Date.now());
    await page.getByRole('button', { name: 'Create repository backup', exact: true }).click();
    await page.getByRole('status').filter({ hasText: 'Completed repository backup selected.' }).waitFor({ timeout: 300000 });
    assert.notEqual(await page.locator('#operator-source').inputValue(), 'live');
    assert.match(await page.getByTestId('source-info').innerText(), /Read-only source/);
    console.log('NATIVE_BACKUP_SELECTION_PASS');
    await page.getByRole('button', { name: 'Create working copy', exact: true }).click();
    await page.getByTestId('source-info').getByText('Writable working copy', { exact: true }).waitFor();
    await page.locator('#operator-scope').fill('/Root/Content/IndexToolsLab');
    await page.getByRole('button', { name: 'Compare subtree', exact: true }).click();
    await page.getByTestId('comparison-summary').waitFor({ timeout: 90000 });
    assert.match(await page.getByTestId('comparison-summary').innerText(), /0 differences/);
    console.log('WORKING_COPY_SUBTREE_COMPARE_PASS');
    assert.ok(!(await page.locator('body').innerText()).includes(token));

    await navigate(base + '/lastactivityid');
    await page.getByRole('button', { name: /Get Index LastActivityId/ }).click();
    await page.waitForFunction(() => Array.from(document.querySelectorAll('p.card-text')).some(p => /LastActivityId:\s*\d+/.test(p.textContent)));
    await page.locator('#offline').check();
    await page.locator('#newId').fill('42');
    assert.equal(await page.getByRole('button', { name: /Set LastActivityId/ }).isDisabled(), true);
    assert.equal(await page.getByRole('button', { name: /Initialize LastActivityId/ }).isDisabled(), true);
    console.log('LEGACY_LIVE_ACTIVITY_WRITE_CONTROLS_DISABLED_PASS');

    if (process.argv.includes('--repairs')) {
      const fixture = JSON.parse(execFileSync('docker', [...compose, 'exec', '-T', 'tools', 'cat', '/state/reports/browser-fixture.json'], { encoding: 'utf8' }));
      await navigate(base + '/operations?report=' + fixture.ReportId);
      assert.equal(await page.locator('#operator-source').inputValue(), fixture.SourceId);
      assert.match(await page.getByTestId('comparison-summary').innerText(), /1 confirmed orphan candidates/);
      await page.getByLabel(/RepairTarget \(ID .*DB only/).check();
      await page.getByLabel(/Confirm OData rebuild for the selected 1 content items/).check();
      await page.getByRole('button', { name: 'Rebuild selected content and verify', exact: true }).click();
      await page.getByText(/Rebuild activity processed; fresh SQL\/index comparison matches/).waitFor({ timeout: 100000 });
      console.log('REPORT_SELECTED_ODATA_REPAIR_VERIFIED_PASS');
      await page.getByLabel(/Confirm deletion of only the previewed orphan documents/).check();
      await page.getByRole('button', { name: 'Clean previewed orphans', exact: true }).click();
      await page.getByRole('status').filter({ hasText: 'Removed 1 orphan documents' }).waitFor({ timeout: 90000 });
      assert.match(await page.getByTestId('comparison-summary').innerText(), /0 confirmed orphan candidates/);
      console.log('WORKING_COPY_ORPHAN_CLEANUP_PASS');
      await page.screenshot({ path: path.join(root, '.private/local/operator-working.png'), fullPage: true });
    }
    console.log('LOCAL_OPERATOR_BROWSER_ACCEPTANCE_PASS');
  } finally { await browser.close(); }
})().catch(error => {
  let message = String(error.message);
  for (const name of ['web-token', 'api-key', 'sql-connection']) {
    const value = fs.readFileSync(path.join(root, '.private/local/tools', name), 'utf8').trim();
    if (value) message = message.split(value).join('[redacted]');
    if (name === 'sql-connection') for (const part of value.split(';')) {
      if (/^(password|pwd)=/i.test(part.trim())) message = message.split(part.slice(part.indexOf('=') + 1)).join('[redacted]');
    }
  }
  console.error('LOCAL_BROWSER_CHECK_FAILED: ' + error.name + ': ' + message.slice(0, 3000)); process.exit(1);
});
