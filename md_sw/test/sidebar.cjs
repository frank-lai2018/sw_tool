const { _electron } = require('playwright-core');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');

(async () => {
  const root = path.resolve(__dirname, '..');
  const temp = await fs.mkdtemp(path.join(os.tmpdir(), 'md-sidebar-'));
  const development = process.argv.includes('--dev');
  let app;
  async function launch(width) {
    app = await _electron.launch({ executablePath: development ? require('electron') : path.join(root, 'dist/win-unpacked/MD Reader.exe'), args: [...(development ? [root] : []), `--user-data-dir=${temp}`] });
    const page = await app.firstWindow(); page.setDefaultTimeout(15000);
    await page.waitForFunction(expected => document.querySelector('#sidebar').getBoundingClientRect().width === expected && document.querySelector('#content h1'), width);
    return page;
  }
  async function drag(page, to) {
    const bounds = await page.locator('#sidebar-resizer').boundingBox();
    const y = bounds.y + bounds.height / 2;
    await page.mouse.move(bounds.x + bounds.width / 2, y); await page.mouse.down();
    await page.mouse.move(to, y, { steps: 12 }); await page.mouse.up();
    await page.evaluate(() => window.reader.preferences({}));
    assert.equal(await page.locator('body').evaluate(body => body.classList.contains('sidebar-resizing')), false);
  }
  try {
    let page = await launch(248);
    await page.evaluate(() => { window.originalHeading = document.querySelector('#content h1'); });
    const originalLeft = await page.locator('main').evaluate(node => node.getBoundingClientRect().left);
    await drag(page, 428);
    assert.equal(await page.locator('#sidebar').evaluate(node => node.getBoundingClientRect().width), 428);
    assert.equal(await page.locator('main').evaluate(node => node.getBoundingClientRect().left), originalLeft + 180);
    assert.equal(await page.evaluate(() => document.querySelector('#content h1') === window.originalHeading), true, 'Resizing must not render a new document');
    await app.close(); page = await launch(428);
    await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].setSize(780, 600));
    await page.waitForFunction(() => document.querySelector('#sidebar').getBoundingClientRect().width <= innerWidth - 460);
    assert.ok(await page.locator('main').evaluate(node => node.getBoundingClientRect().width) >= 460);
    await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].setSize(1260, 850));
    await page.waitForFunction(() => document.querySelector('#sidebar').getBoundingClientRect().width === 428);
    await page.click('#toggle-sidebar'); assert.equal(await page.locator('#sidebar-resizer').isHidden(), true);
    await page.click('#toggle-sidebar'); assert.equal(await page.locator('#sidebar-resizer').isVisible(), true);
    await drag(page, 40);
    assert.equal(await page.locator('#sidebar').evaluate(node => node.getBoundingClientRect().width), 200);
    await drag(page, (await page.evaluate(() => innerWidth)) - 5);
    assert.equal(await page.locator('#sidebar').evaluate(node => node.getBoundingClientRect().width), 600);
    await page.locator('#sidebar-resizer').focus(); await page.keyboard.press('ArrowLeft');
    assert.equal(await page.locator('#sidebar-resizer').getAttribute('aria-valuenow'), '584');
    await page.keyboard.press('Shift+ArrowLeft');
    assert.equal(await page.locator('#sidebar-resizer').getAttribute('aria-valuenow'), '544');
    await page.keyboard.press('Home'); assert.equal(await page.locator('#sidebar-resizer').getAttribute('aria-valuenow'), '200');
    await page.keyboard.press('End'); assert.equal(await page.locator('#sidebar-resizer').getAttribute('aria-valuenow'), '600');
    await page.locator('#sidebar-resizer').dblclick();
    assert.equal(await page.locator('#sidebar').evaluate(node => node.getBoundingClientRect().width), 248);
    await drag(page, 380);
    await page.screenshot({ path: path.join(root, 'test-output/sidebar-resize.png') });
    console.log('PASS: real mouse drag, pointer release outside handle, document DOM retained, width persists on restart, window-size clamping/restoration, hide/show, limits, keyboard resizing, and double-click reset.');
  } finally { if (app) await app.close(); await fs.rm(temp, { recursive: true, force: true }); }
})().catch(error => { console.error(error); process.exitCode = 1; });
