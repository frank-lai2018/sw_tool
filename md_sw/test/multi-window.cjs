const { _electron } = require('playwright-core');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');

(async () => {
  const root = path.resolve(__dirname, '..');
  const temp = await fs.mkdtemp(path.join(os.tmpdir(), 'md-multi-'));
  const development = process.argv.includes('--dev');
  const executablePath = development ? require('electron') : path.join(root, 'dist/win-unpacked/MD Reader.exe');
  const args = [...(development ? [root] : []), `--user-data-dir=${path.join(temp, 'profile')}`];
  const fileA = path.join(temp, 'A.md'), fileB = path.join(temp, 'B.md');
  await fs.writeFile(fileA, '# 文件 A\n\nOnlyInA\n\n[下一份](next.md)');
  await fs.writeFile(fileB, '# 文件 B\n\nOnlyInB');
  await fs.writeFile(path.join(temp, 'next.md'), '# 相對連結文件');
  let app, other;
  const errors = [];
  async function ready(page, title) {
    page.setDefaultTimeout(15000);
    page.on('pageerror', error => errors.push(error.message));
    await page.waitForFunction(text => document.querySelector('#content h1')?.textContent === text, title);
  }
  try {
    app = await _electron.launch({ executablePath, args: [...args, fileA] });
    const first = await app.firstWindow(); await ready(first, '文件 A');
    const secondPromise = app.waitForEvent('window');
    await first.click('#new-window');
    const second = await secondPromise; await ready(second, '文件 A');
    await second.evaluate(file => window.reader.openFile(file), fileB); await ready(second, '文件 B');
    assert.equal(await first.locator('#content h1').textContent(), '文件 A');
    await first.click('#search-button'); await first.fill('#search-input', 'OnlyInA');
    await first.waitForFunction(() => document.querySelector('#search-result').textContent === '1 / 1');
    assert.equal(await second.locator('#search-bar').isHidden(), true);
    const thirdPromise = app.waitForEvent('window');
    await app.evaluate(({ Menu }) => {
      const item = Menu.getApplicationMenu().items[0].submenu.items.find(item => item.label === '新視窗');
      if (item.accelerator !== 'CmdOrCtrl+N') throw new Error('Missing Ctrl+N shortcut');
      item.click();
    });
    const third = await thirdPromise; await ready(third, '文件 B');
    await third.close();
    await first.evaluate(() => window.reader.openLink('next.md')); await ready(first, '相對連結文件');
    assert.equal(await second.locator('#content h1').textContent(), '文件 B');
    await first.close();
    await fs.writeFile(fileB, '# 文件 B 已更新');
    await second.click('#reload'); await ready(second, '文件 B 已更新');
    assert.equal(app.windows().length, 1);
    // Launching the executable again with the SAME profile must not redirect or quit.
    other = await _electron.launch({ executablePath, args: [...args, fileA] });
    const independent = await other.firstWindow(); await ready(independent, '文件 A');
    assert.equal(await second.locator('#content h1').textContent(), '文件 B 已更新');
    await other.close(); other = null;
    await second.evaluate(() => window.reader.reload()); await ready(second, '文件 B 已更新');
    assert.deepEqual(errors, []);
    console.log('PASS: button and Ctrl+N menu create independent windows; documents, search, links and reload stay isolated; closing original window preserves others; repeated EXE launch with same profile works.');
  } finally {
    if (other) await other.close();
    if (app) await app.close();
    await fs.rm(temp, { recursive: true, force: true });
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
