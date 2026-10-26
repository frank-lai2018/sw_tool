const { _electron } = require('playwright-core');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');

(async () => {
  const root = path.resolve(__dirname, '..');
  const temp = await fs.mkdtemp(path.join(os.tmpdir(), 'md-reader-desktop-'));
  const output = path.join(root, 'test-output');
  await fs.mkdir(output, { recursive: true });
  const errors = [];
  const development = process.argv.includes('--dev');
  const app = await _electron.launch({ executablePath: development ? require('electron') : path.join(root, 'dist', 'win-unpacked', 'MD Reader.exe'), args: [...(development ? [root] : []), `--user-data-dir=${path.join(temp, 'profile')}`] });
  try {
    const page = await app.firstWindow();
    page.setDefaultTimeout(15000);
    page.on('pageerror', error => errors.push(error.message));
    await page.waitForSelector('#content h1');
    await page.waitForFunction(() => window.reader && document.querySelector('#content pre code .hljs-keyword'));
    assert.match(await page.locator('#content').innerText(), /每一份筆記/);
    assert.ok(await page.locator('#toc a').count() >= 4);
    assert.equal(await page.locator('#content input[type=checkbox]').count(), 3);
    await page.screenshot({ path: path.join(output, 'welcome.png') });
    await page.click('#theme-button');
    assert.equal(await page.locator('body').evaluate(body => body.classList.contains('dark')), true);
    await page.screenshot({ path: path.join(output, 'dark.png') });
    await page.click('#theme-button');
    await page.click('#font-larger');
    assert.equal(await page.locator('#font-size').textContent(), '18');
    await page.click('#font-smaller');
    const fixture = path.join(temp, '中文測試.md');
    await fs.writeFile(path.join(temp, 'pixel.png'), Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a0ioAAAAASUVORK5CYII=', 'base64'));
    await fs.writeFile(fixture, '# 中文文件\n\n測試內容 unique-find-needle\n\n[前往章節](#第二章)\n\n## 第二章\n\n```javascript\nconst answer = 42;\n```\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n![本機圖片](pixel.png)\n\n![遠端圖片](https://example.com/should-not-load.png)\n\n<img src=x onerror="window.injected=true">\n<script>window.injected=true</script>\n<div id="source">安全測試</div>\n<input type="text" value="forbidden">');
    const remoteRequests = [];
    page.on('request', request => { if (/^https?:/.test(request.url())) remoteRequests.push(request.url()); });
    assert.equal(await page.evaluate(file => window.reader.openFile(file), fixture), true);
    await page.waitForFunction(() => document.querySelector('#filename').textContent === '中文測試.md');
    await page.waitForSelector('#content img[src^="data:image/png"]');
    assert.equal(await page.locator('#content table').count(), 1);
    assert.equal(await page.locator('#content script,#content [onerror],#content input[type=text]').count(), 0);
    assert.equal(await page.evaluate(() => window.injected), undefined);
    assert.deepEqual(remoteRequests, []);
    assert.equal(await page.locator('#source').count(), 1);
    assert.equal(await page.locator('#content .image-note').count(), 2);
    await page.click('#source-button');
    assert.equal(await page.locator('#source').isVisible(), true);
    assert.match(await page.locator('#source').textContent(), /# 中文文件/);
    await page.click('#source-button');
    await page.click('#search-button');
    await page.fill('#search-input', 'unique-find-needle');
    await page.waitForFunction(() => document.querySelector('#search-result').textContent === '1 / 1');
    assert.equal(await page.evaluate(() => CSS.highlights.get('search-current').size), 1);
    await page.fill('#search-input', '文件');
    await page.waitForFunction(() => document.querySelector('#search-result').textContent === '1 / 1');
    await page.fill('#search-input', '章');
    await page.waitForFunction(() => document.querySelector('#search-result').textContent === '1 / 2');
    await page.keyboard.press('Enter');
    await page.waitForFunction(() => document.querySelector('#search-result').textContent === '2 / 2');
    await page.keyboard.press('Enter');
    await page.waitForFunction(() => document.querySelector('#search-result').textContent === '1 / 2');
    await page.fill('#search-input', '[no.match]');
    await page.waitForFunction(() => document.querySelector('#search-result').textContent === '0 / 0');
    await page.keyboard.press('Escape');
    assert.equal(await page.locator('#search-bar').isHidden(), true);
    await page.click('#recent-tab');
    assert.match(await page.locator('#recent').innerText(), /中文測試.md/);
    await page.click('#toc-tab');
    await fs.writeFile(fixture, '# 更新後文件\n\n重新載入成功');
    await page.click('#reload');
    await page.waitForFunction(() => document.querySelector('#content h1')?.textContent === '更新後文件');
    const invalid = await page.evaluate(file => window.reader.openFile(file), path.join(temp, 'missing.md'));
    assert.equal(invalid, false);
    assert.match(await page.locator('#toast').textContent(), /找不到這份文件/);
    assert.equal(await page.locator('#content h1').textContent(), '更新後文件');
    // Exercise the actual Chromium PDF path without opening a printer dialog.
    const pdf = await app.evaluate(async ({ BrowserWindow }) => {
      return (await BrowserWindow.getAllWindows()[0].webContents.printToPDF({ printBackground: true })).toString('base64');
    });
    assert.ok(Buffer.from(pdf, 'base64').length > 1000);
    await fs.writeFile(path.join(output, 'print.pdf'), Buffer.from(pdf, 'base64'));
    await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].setSize(780, 600));
    await page.screenshot({ path: path.join(output, 'small-window.png') });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
    assert.deepEqual(errors, []);
    console.log(`PASS: ${development ? 'development' : 'packaged'} desktop app, Chinese rendering, syntax highlighting, tasks, themes, font size, TOC, local images, HTML sanitizing, offline behavior, source view, search, recent files, reload, error recovery, PDF rendering, compact layout.`);
  } catch (error) {
    const page = app.windows()[0];
    if (page) { console.error(await page.evaluate(() => ({ file: document.querySelector('#filename')?.textContent, search: document.querySelector('#search-result')?.textContent }))); await page.screenshot({ path: path.join(output, 'failure.png') }); }
    throw error;
  } finally {
    await app.close();
    await fs.rm(temp, { recursive: true, force: true });
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
