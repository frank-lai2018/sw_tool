const { _electron } = require('playwright-core');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');

(async () => {
  const root = path.resolve(__dirname, '..');
  const temp = await fs.mkdtemp(path.join(os.tmpdir(), 'md-width-'));
  const development = process.argv.includes('--dev');
  let app;
  async function launch(expected) {
    app = await _electron.launch({ executablePath: development ? require('electron') : path.join(root, 'dist/win-unpacked/MD Reader.exe'), args: [...(development ? [root] : []), `--user-data-dir=${temp}`] });
    const page = await app.firstWindow();
    page.setDefaultTimeout(15000);
    await page.waitForFunction(width => document.documentElement.style.getPropertyValue('--page-width') === width, expected);
    return page;
  }
  async function setWidth(page, value) {
    await page.locator('#page-width').evaluate((input, width) => { input.value = width; input.dispatchEvent(new Event('input', { bubbles: true })); }, value);
    await page.evaluate(() => window.reader.preferences({}));
  }
  try {
    let page = await launch('860px');
    await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].setContentSize(2100, 1000));
    await page.locator('#page-width').focus(); await page.keyboard.press('Home');
    await page.waitForFunction(() => document.querySelector('#content').getBoundingClientRect().width === 600);
    assert.equal(await page.locator('#page-width-value').textContent(), '600 px');
    const left = await page.locator('#content').evaluate(node => node.getBoundingClientRect().left);
    await page.click('#full-width');
    await page.waitForFunction(() => {
      const area = document.querySelector('#scroll-area'), style = getComputedStyle(area);
      const available = area.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
      return Math.abs(document.querySelector('#content').getBoundingClientRect().width - available) < 1;
    });
    assert.equal(await page.locator('#full-width').getAttribute('aria-pressed'), 'true');
    assert.ok(await page.locator('#content').evaluate(node => node.getBoundingClientRect().left) < left);
    await page.click('#full-width');
    await page.waitForFunction(() => document.querySelector('#content').getBoundingClientRect().width === 600);
    await setWidth(page, 1600);
    await page.waitForFunction(() => document.querySelector('#content').getBoundingClientRect().width === 1600);
    await page.click('#source-button');
    assert.equal(await page.locator('#source').evaluate(node => node.getBoundingClientRect().width), 1600);
    await page.click('#source-button');
    await app.close();
    page = await launch('1600px');
    assert.equal(await page.locator('#page-width').inputValue(), '1600');
    await page.click('#full-width');
    await page.evaluate(() => window.reader.preferences({}));
    await app.close();
    page = await launch('100%');
    assert.equal(await page.locator('#page-width-value').textContent(), '滿版');
    await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].setSize(780, 600));
    await page.screenshot({ path: path.join(root, 'test-output', 'width-small.png') });
    assert.equal(await page.evaluate(() => document.querySelector('#scroll-area').scrollWidth > document.querySelector('#scroll-area').clientWidth), false);
    await setWidth(page, 1600);
    assert.equal(await page.evaluate(() => document.querySelector('#scroll-area').scrollWidth > document.querySelector('#scroll-area').clientWidth), false);
    await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].setSize(1260, 850));
    await setWidth(page, 860);
    await page.screenshot({ path: path.join(root, 'test-output', 'width-controls.png') });
    console.log('PASS: width slider changes rendered page geometry; full width toggles correctly; source view follows width; fixed and full width persist across restarts; narrow windows avoid horizontal overflow.');
  } catch (error) {
    const page = app?.windows()[0];
    if (page) {
      console.error(await page.evaluate(() => ({ viewport: innerWidth, pageWidth: document.querySelector('#content').getBoundingClientRect().width, setting: document.documentElement.style.getPropertyValue('--page-width'), label: document.querySelector('#page-width-value').textContent, available: document.querySelector('#scroll-area').clientWidth })));
      await page.screenshot({ path: path.join(root, 'test-output', 'width-failure.png') });
    }
    throw error;
  } finally {
    if (app) await app.close();
    await fs.rm(temp, { recursive: true, force: true });
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
