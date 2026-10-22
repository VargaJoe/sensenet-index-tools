// Optional real-browser acceptance. Requires Node + Playwright and a Chromium browser.
const { chromium } = require(process.env.INDEXTOOLS_PLAYWRIGHT_MODULE || 'playwright');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { execFileSync } = require('node:child_process');
const root = path.resolve(__dirname, '../..');
const privateDir = path.join(root, '.private/multi-repository-lab');
const ports = JSON.parse(fs.readFileSync(path.join(privateDir, 'ports.json')));
const base = `http://127.0.0.1:${ports.tools}`;
const compose = ['compose', '-p', 'index-tools-multirepo-lab', '-f', path.join(privateDir, 'compose.json')];
const results = { repositories: {} };
const tool = (...args) => execFileSync('docker', [...compose, 'exec', '-T', 'tools', ...args], { encoding: 'utf8' });
const state = name => JSON.parse(tool('cat', '/state/data/' + name + '.json'));

(async () => {
  const browser = await chromium.launch({ headless: true,
    ...(process.env.INDEXTOOLS_BROWSER_CHANNEL ? { channel: process.env.INDEXTOOLS_BROWSER_CHANNEL } : {}) });
  try {
    const contexts = await Promise.all([browser.newContext({ viewport: { width: 1440, height: 1100 } }),
      browser.newContext({ viewport: { width: 1440, height: 1100 } })]);
    const pages = [];
    for (const context of contexts) {
      const token = fs.readFileSync(path.join(privateDir, 'tools/web-token'), 'utf8').trim();
      const login = await context.request.post(base + '/login', { form: { token } });
      assert.ok(login.ok());
      const page = await context.newPage(); page.setDefaultTimeout(30000); pages.push(page);
    }
    const navigate = async (page, route) => {
      await page.goto(base + route, { waitUntil: 'networkidle' });
      await page.waitForFunction(() => !document.querySelector('#active-repository').closest('fieldset').disabled);
    };
    const choose = async (page, label) => {
      await page.locator('#active-repository').selectOption({ label });
      await page.waitForFunction(label => document.querySelector('#active-repository').selectedOptions[0].textContent === label, label);
    };
    results.stage = 'open two browsers';
    for (const page of pages) await navigate(page, '/operations');
    results.stage = 'choose profiles';
    await choose(pages[0], 'Repo A'); await choose(pages[1], 'Repo B');
    const ids = await Promise.all(pages.map(p => p.locator('#active-repository').inputValue()));
    for (const [position, letter] of ['a', 'b'].entries()) {
      const page = pages[position], other = pages[1 - position];
      results.stage = 'verify profile ' + letter;
      await page.waitForFunction(letter => {
        const value = document.querySelector('[data-testid="source-info"] code').textContent;
        return value === `/source/repo-${letter}/LocalIndex` || value.startsWith(`/source/repo-${letter}/LocalIndex/`);
      }, letter);
      await page.getByRole('button', { name: 'Connection settings', exact: true }).click();
      assert.equal(await page.locator('#lastactivity-connection-string').getAttribute('type'), 'password');
      assert.match(await page.locator('#lastactivity-connection-string').inputValue(), new RegExp('Database=IndexToolsRepo' + letter.toUpperCase()));
      assert.equal(await page.locator('#context-url').inputValue(), 'http://repo-' + letter);
      assert.equal(await page.locator('#context-backup-source').inputValue(), `/source/repo-${letter}/IndexBackups`);
      await page.getByRole('button', { name: 'Connection settings', exact: true }).click();
      await page.getByRole('button', { name: 'Prepare index source', exact: true }).click();
      const folder = 'browser-' + letter + '-' + Date.now();
      results.stage = 'native backup ' + letter;
      await page.locator('#backup-folder').fill(folder);
      await page.getByRole('button', { name: 'Create repository backup', exact: true }).click();
      await page.waitForFunction(folder => document.querySelector('[data-testid="source-info"] code').textContent.includes('/' + folder), folder, { timeout: 120000 });
      const backupId = await page.locator('#operator-source').inputValue();
      const backupPath = await page.getByTestId('source-info').locator('code').innerText();
      assert.match(backupPath, new RegExp('^/source/repo-' + letter + '/IndexBackups/'));
      const hashes = tool('/bin/sh', '-c', 'find "$1" -type f ! -name write.lock -exec sha256sum {} + | sort', 'sh', backupPath);
      assert.ok(hashes.length > 100);
      assert.equal(await other.locator('#active-repository').inputValue(), ids[1 - position]);
      assert.equal(await other.locator('#operator-source option[value="' + backupId + '"]').count(), 0);
      await page.getByRole('button', { name: 'Read LastActivityId', exact: true }).click();
      results.stage = 'read and compare ' + letter;
      await page.getByTestId('activity-result').waitFor();
      const activity = await page.getByTestId('activity-result').locator('strong').allTextContents();
      assert.equal(activity.length, 2); assert.equal(activity[0], activity[1]);
      await page.getByRole('button', { name: 'Compare subtree', exact: true }).click();
      await page.getByTestId('comparison-summary').waitFor();
      assert.match(await page.getByTestId('comparison-summary').innerText(), /0 differences · 0 confirmed orphan candidates/);
      assert.match(await page.locator('body').innerText(), new RegExp('/repo' + letter, 'i'));
      await page.getByRole('button', { name: 'Create working copy', exact: true }).click();
      results.stage = 'working copy ' + letter;
      await page.getByTestId('source-info').getByText('Writable working copy', { exact: true }).waitFor();
      const copyId = await page.locator('#operator-source').inputValue();
      await navigate(page, '/lastactivityid');
      assert.equal(await page.locator('#operator-source').inputValue(), copyId);
      await page.locator('#newId').fill('23456'); await page.locator('#offline').check();
      await page.getByRole('button', { name: 'Set LastActivityId', exact: true }).click();
      results.stage = 'metadata correction ' + letter;
      await page.waitForFunction(() => [...document.querySelectorAll('p.card-text')].some(e => e.textContent.includes('23456')));
      await page.getByRole('button', { name: /Get Database LastActivityId/ }).click();
      await page.getByRole('heading', { name: /Database LastActivityId/ }).waitFor();
      assert.match(await page.getByRole('heading', { name: /Database LastActivityId/ }).locator('..').innerText(), new RegExp(activity[1]));
      assert.equal(tool('/bin/sh', '-c', 'find "$1" -type f ! -name write.lock -exec sha256sum {} + | sort', 'sh', backupPath), hashes);
      await navigate(page, '/validation');
      results.stage = 'validation ' + letter;
      await page.getByRole('button', { name: 'Start Validation', exact: false }).click();
      await page.locator('iframe[title="Index validation report"]').waitFor();
      await page.frameLocator('iframe[title="Index validation report"]').getByRole('heading', { name: 'SenseNet Index Validation Report', exact: true }).waitFor();
      assert.equal(await page.locator('iframe[title="Index validation report"]').getAttribute('sandbox'), '');
      const countBefore = state('reports').length;
      for (const [name, extension] of [['Download HTML', 'html'], ['Download Markdown', 'md']]) {
        const pending = page.waitForEvent('download');
        await page.getByRole('button', { name, exact: true }).click();
        const download = await pending; assert.ok(download.suggestedFilename().endsWith('.' + extension));
        await download.saveAs(path.join(privateDir, 'repo-' + letter + '-validation.' + extension));
      }
      assert.equal(state('reports').length, countBefore);
      await navigate(page, '/subtree-check');
      results.stage = 'subtree ' + letter;
      await page.locator('#format').selectOption('html');
      await page.getByRole('button', { name: /Start Subtree Check/ }).click();
      await page.locator('iframe[title="Subtree comparison report"]').waitFor();
      await page.frameLocator('iframe[title="Subtree comparison report"]').getByRole('heading', { name: 'SenseNet Index Check Report', exact: true }).waitFor();
      assert.equal(await page.locator('iframe[title="Subtree comparison report"]').getAttribute('sandbox'), '');
      assert.equal(await page.locator('#operator-source').inputValue(), copyId);
      await page.locator('iframe[title="Subtree comparison report"]').scrollIntoViewIfNeeded();
      await page.screenshot({ path: path.join(privateDir, 'repo-' + letter + '-subtree.png'), fullPage: true });
      await navigate(page, '/operations');
      assert.equal(await page.locator('#active-repository').inputValue(), ids[position]);
      assert.equal(await page.locator('#operator-source').inputValue(), copyId);
      results.repositories['Repo ' + letter.toUpperCase()] = {
        backupId, backupPath, copyId, nativeBackup: true, activityMatchesDatabase: true,
        arbitraryMetadataValue: 23456, originalBackupHashesPreserved: true,
        validation: true, subtreeHtml: true, bothExportFormats: true, reloadPreservesSelection: true
      };
      console.log('REPO_' + letter.toUpperCase() + '_REAL_DOCKER_WORKFLOW_PASS');
    }
    results.stage = 'reopen another repository report';
    const savedComparison = state('reports').find(report => report.ConfigurationId === ids[1] && report.Parameters.ComparisonId);
    assert.ok(savedComparison);
    await choose(pages[0], 'Repo A');
    await navigate(pages[0], '/reports/' + savedComparison.Id);
    await pages[0].getByRole('link', { name: 'Review repair candidates', exact: true }).click();
    await pages[0].getByTestId('comparison-summary').waitFor();
    assert.equal(await pages[0].locator('#active-repository').inputValue(), ids[1]);
    assert.equal(await pages[0].locator('#operator-source').inputValue(), savedComparison.Parameters.IndexSourceId);
    assert.equal(await pages[1].locator('#operator-source').inputValue(), results.repositories['Repo B'].copyId);
    results.reportReopenRestoresRepositoryAndSource = true;
    const saved = state('configurations');
    for (const name of ['Repo A', 'Repo B']) {
      const profile = saved.find(p => p.Name === name); assert.ok(profile);
      assert.ok(profile.ConnectionString.startsWith('protected:v1:')); assert.ok(profile.ApiKey.startsWith('protected:v1:'));
    }
    const containerId = execFileSync('docker', [...compose, 'ps', '-q', 'tools'], { encoding: 'utf8' }).trim();
    const container = JSON.parse(execFileSync('docker', ['inspect', containerId], { encoding: 'utf8' }))[0];
    assert.equal(container.Config.User, '1654:1654'); assert.ok(container.HostConfig.ReadonlyRootfs);
    for (const letter of 'ab') assert.equal(container.Mounts.find(m => m.Destination === '/source/repo-' + letter).RW, false);
    assert.ok(container.Mounts.every(m => !/docker\.sock$/.test(m.Destination)));
    results.independentBrowsers = true; results.protectedSavedProfiles = true; results.readonlySourceMounts = true;
    results.complete = true; results.stage = 'complete'; results.completedUtc = new Date().toISOString();
  } finally {
    if (!results.complete) for (const [i, page] of (await browser.contexts().flatMap(c => c.pages())).entries()) {
      await page.screenshot({ path: path.join(privateDir, 'failure-' + i + '.png'), fullPage: true }).catch(() => {});
      fs.writeFileSync(path.join(privateDir, 'failure-' + i + '.txt'), await page.locator('body').innerText().catch(() => 'unavailable'));
    }
    fs.writeFileSync(path.join(privateDir, 'browser-acceptance.json'), JSON.stringify(results, null, 2));
    await browser.close();
  }
  console.log('TWO_REPOSITORY_BROWSER_ACCEPTANCE_PASS');
})().catch(error => {
  let message = String(error.stack || error.message);
  const credentials = JSON.parse(fs.readFileSync(path.join(privateDir, 'credentials.json')));
  for (const secret of Object.values(credentials)) message = message.split(secret).join('[redacted]');
  for (const letter of 'ab') {
    const key = fs.readFileSync(path.join(privateDir, 'tools/api-key-' + letter), 'utf8').trim();
    if (key) message = message.split(key).join('[redacted]');
  }
  console.error('MULTIREPO_BROWSER_FAILED ' + error.name + ': ' + message.slice(0, 2200)); process.exitCode = 1;
});
