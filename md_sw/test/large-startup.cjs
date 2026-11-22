const { _electron } = require('playwright-core');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');

(async () => {
  const root = path.resolve(__dirname, '..');
  const temp = await fs.mkdtemp(path.join(os.tmpdir(), 'md-large-startup-'));
  const file = process.argv.find(arg => arg.startsWith('--file='))?.slice(7) || path.join(temp, '大型中文文件.md');
  if (!process.argv.some(arg => arg.startsWith('--file='))) {
    await fs.writeFile(file, '# 大型文件\n\n| 編號 | 說明 |\n|---|---|\n' + `| 123 | 中文測試資料 ${'abc'.repeat(120)} |\n`.repeat(60000));
  }
  const missing = path.join(temp, '不存在的文件.md');
  const oversized = path.join(temp, '過大.md');
  const handle = await fs.open(oversized, 'w');
  await handle.truncate(100 * 1024 * 1024 + 1); await handle.close();
  const packaged = process.argv.includes('--packaged');
  const app = await _electron.launch({
    executablePath: packaged ? path.join(root, 'dist/win-unpacked/MD Reader.exe') : require('electron'),
    args: [...(packaged ? [] : [root]), `--user-data-dir=${path.join(temp, 'profile')}`, missing]
  });
  try {
    const page = await app.firstWindow();
    await page.waitForFunction(() => !document.querySelector('#open-error').hidden);
    assert.equal(await page.locator('#welcome-banner').isHidden(), true);
    assert.equal(await page.locator('#content').textContent(), '');
    await page.waitForTimeout(4500);
    assert.match(await page.locator('#open-error').textContent(), /找不到/);
    assert.equal(await page.locator('#open-error').isVisible(), true);
    await page.evaluate(file => window.reader.openFile(file), oversized);
    assert.match(await page.locator('#open-error').textContent(), /100 MiB/);
    const start = Date.now();
    await page.evaluate(file => window.reader.openFile(file), file);
    await page.waitForFunction(() => document.querySelector('#document-state').textContent === '分頁閱讀', null, { timeout: 120000 });
    assert.equal(await page.locator('#filename').textContent(), path.basename(file));
    assert.equal(await page.locator('#open-error').isHidden(), true);
    const pages = Number(await page.locator('#page-number').getAttribute('max'));
    assert.ok(pages > 10);
    const nodes = await page.locator('#content *').count(); assert.ok(nodes > 0 && nodes < 10000);
    await page.fill('#page-number', String(pages)); await page.press('#page-number', 'Enter');
    await page.waitForFunction(() => document.querySelector('#page-next').disabled);
    await page.click('#source-button');
    assert.ok((await page.locator('#source').textContent()).length <= 64000);
    await page.evaluate(file => window.reader.openFile(file), missing);
    assert.equal(await page.locator('#filename').textContent(), path.basename(file));
    assert.equal(await page.locator('#open-error').isVisible(), true);
    console.log(JSON.stringify({ result: 'PASS', bytes: (await fs.stat(file)).size, pages, initialNodes: nodes, elapsedMs: Date.now() - start }));
  } finally {
    await app.close(); await fs.rm(temp, { recursive: true, force: true });
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
