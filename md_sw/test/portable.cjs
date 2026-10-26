const { chromium } = require('playwright-core');
const { spawn } = require('node:child_process');
const { once } = require('node:events');
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const net = require('node:net');
const assert = require('node:assert/strict');

(async () => {
  const root = path.resolve(__dirname, '..');
  const temp = await fs.mkdtemp(path.join(os.tmpdir(), 'md-portable-'));
  const file = path.join(temp, '免安裝啟動測試.md');
  await fs.writeFile(file, '# 免安裝版啟動成功\n\nWindows Markdown 閱讀器。');
  const server = net.createServer();
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const port = server.address().port;
  await new Promise(resolve => server.close(resolve));
  const executable = path.join(root, 'dist', `MD-Reader-${require('../package.json').version}-Windows.exe`);
  const child = spawn(executable, [`--remote-debugging-port=${port}`, `--user-data-dir=${path.join(temp, 'profile')}`, file], { windowsHide: true, stdio: 'ignore' });
  const exited = once(child, 'exit');
  let browser;
  try {
    const deadline = Date.now() + 45000;
    const endpoint = `http://127.0.0.1:${port}`;
    while (true) {
      try { await fetch(`${endpoint}/json/version`, { signal: AbortSignal.timeout(1000) }); break; }
      catch (error) { if (Date.now() > deadline || child.exitCode !== null) throw new Error('免安裝版未能啟動：' + error.message); await new Promise(resolve => setTimeout(resolve, 400)); }
    }
    browser = await chromium.connectOverCDP(endpoint);
    const page = browser.contexts()[0].pages()[0];
    await page.waitForFunction(() => document.querySelector('#filename')?.textContent === '免安裝啟動測試.md', null, { timeout: 15000 });
    assert.equal(await page.locator('#content h1').textContent(), '免安裝版啟動成功');
    await page.screenshot({ path: path.join(root, 'test-output', 'portable.png') });
    const nextPage = browser.contexts()[0].waitForEvent('page');
    await page.click('#new-window');
    const second = await nextPage;
    await second.waitForFunction(() => document.querySelector('#content h1')?.textContent === '免安裝版啟動成功', null, { timeout: 15000 });
    await page.close();
    await second.click('#reload');
    assert.equal(await second.locator('#content h1').textContent(), '免安裝版啟動成功');
    assert.equal(child.exitCode, null);
    await second.close();
    const [code] = await Promise.race([exited, new Promise((_, reject) => setTimeout(() => reject(new Error('程式未能正常結束')), 15000).unref())]);
    assert.equal(code, 0);
    console.log('PASS: portable EXE extracts, opens a Unicode Markdown file, creates another window, keeps running after the first closes, and exits cleanly when the final window closes.');
  } finally {
    if (browser) await browser.close();
    if (child.exitCode === null) child.kill();
    await fs.rm(temp, { recursive: true, force: true });
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
