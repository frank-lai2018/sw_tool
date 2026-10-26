const { _electron } = require('playwright-core');
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const assert = require('node:assert/strict');

(async () => {
  const root = path.resolve(__dirname, '..');
  const temp = await fs.mkdtemp(path.join(os.tmpdir(), 'md-memory-'));
  const baseline = process.argv.includes('--baseline');
  const development = process.argv.includes('--dev');
  const settings = JSON.parse(await fs.readFile(path.join(process.env.APPDATA, 'md-reader/settings.json'), 'utf8'));
  const file = settings.lastFile;
  assert.ok((await fs.stat(file)).size > 1000000, 'Benchmark needs the actual large document');
  let app;
  try {
    app = await _electron.launch({ executablePath: development ? require('electron') : path.join(root, 'dist/win-unpacked/MD Reader.exe'), args: [...(development ? [root] : []), `--user-data-dir=${temp}`, file], timeout: 120000 });
    const page = await app.firstWindow();
    const start = Date.now();
    await page.waitForFunction(name => document.querySelector('#filename').textContent === name && document.querySelector('#content').children.length > 0 && !['讀取中…', '讀取失敗'].includes(document.querySelector('#document-state').textContent) && document.querySelector('#word-count').textContent, path.basename(file), { timeout: 120000 });
    await page.waitForTimeout(1500);
    const snapshot = async () => ({
      processes: await app.evaluate(({ app }) => app.getAppMetrics().map(p => ({ type: p.type, pid: p.pid, memory: p.memory }))),
      document: await page.evaluate(() => ({ nodes: document.querySelector('#content').querySelectorAll('*').length, sourceCharacters: document.querySelector('#source').textContent.length, pageLabel: document.querySelector('#page-label')?.textContent, heapBytes: performance.memory?.usedJSHeapSize }))
    });
    const result = { file: path.basename(file), elapsedMs: Date.now() - start, initial: await snapshot() };
    if (!baseline) {
      assert.ok(result.initial.document.nodes < 10000);
      assert.ok(result.initial.document.nodes > 0);
      assert.ok(parseInt(await page.locator('#page-number').getAttribute('max')) > 10);
      assert.equal(result.initial.document.sourceCharacters, 0);
      for (let n = 0; n < 10; n++) await page.click('#page-next');
      await page.waitForTimeout(1000);
      result.afterTenPages = await snapshot();
      assert.ok(result.afterTenPages.document.nodes < 10000);
      for (let n = 0; n < 90; n++) await page.click('#page-next');
      await page.waitForTimeout(1500);
      result.afterHundredPages = await snapshot();
      assert.ok(result.afterHundredPages.document.nodes < 10000);
      assert.ok(result.afterHundredPages.processes.find(p => p.type === 'Tab').memory.privateBytes < 700 * 1024, 'Renderer memory must stay bounded after 100 pages');
      await page.click('#source-button');
      assert.ok((await page.locator('#source').textContent()).length <= 64000);
      await page.click('#source-button');
      assert.equal(await page.locator('#source').textContent(), '');
    }
    await fs.mkdir(path.join(root, 'test-output'), { recursive: true });
    await fs.writeFile(path.join(root, 'test-output', baseline ? 'memory-before.json' : 'memory-after.json'), JSON.stringify(result, null, 2));
    console.log(JSON.stringify(result, null, 2));
  } finally { if (app) await app.close(); await fs.rm(temp, { recursive: true, force: true }); }
})().catch(error => { console.error(error); process.exitCode = 1; });
