const { app, BrowserWindow, ipcMain, dialog, shell, Menu } = require('electron');
const path = require('node:path');
const fs = require('node:fs');
const { readDocument, saveDocument, readLocalImage, isDocument } = require('./files.cjs');
const { getExecutablePath, setWindowsIntegration } = require('./windows-integration.cjs');

const windows = new Map();
const savingPaths = new Set();
let settings = { theme: 'light', fontSize: 17, pageWidth: 860, sidebarWidth: 248, recent: [], lastFile: null };
const pendingFile = process.argv.slice(app.isPackaged ? 1 : 2).find(isDocument);
let integrationBusy = false;
app.whenReady().then(() => {
  loadSettings();
  installMenu();
  createWindow(pendingFile);
});
function saveSettings() {
  try { fs.writeFileSync(path.join(app.getPath('userData'), 'settings.json'), JSON.stringify(settings, null, 2)); } catch {}
}
function publicSettings() { return { ...settings, recent: settings.recent.slice(0, 12) }; }
function loadSettings() {
  try {
    const stored = JSON.parse(fs.readFileSync(path.join(app.getPath('userData'), 'settings.json'), 'utf8'));
    settings.theme = stored.theme === 'dark' ? 'dark' : 'light';
    settings.fontSize = Math.max(13, Math.min(24, Number(stored.fontSize) || 17));
    if (Number.isFinite(stored.pageWidth)) settings.pageWidth = stored.pageWidth === 0 ? 0 : Math.max(600, Math.min(1600, Math.round(stored.pageWidth / 20) * 20));
    if (Number.isFinite(stored.sidebarWidth)) settings.sidebarWidth = Math.max(200, Math.min(600, Math.round(stored.sidebarWidth)));
    settings.recent = Array.isArray(stored.recent) ? stored.recent.filter(isDocument).slice(0, 12) : [];
    settings.lastFile = isDocument(stored.lastFile) ? stored.lastFile : null;
  } catch {}
}
function focusedWindow() {
  const window = BrowserWindow.getFocusedWindow();
  return windows.get(window?.webContents.id) || windows.values().next().value;
}
function installMenu() {
  Menu.setApplicationMenu(Menu.buildFromTemplate([
    { label: '檔案', submenu: [
      { label: '新視窗', accelerator: 'CmdOrCtrl+N', click: () => createWindow() },
      { label: '開啟…', accelerator: 'CmdOrCtrl+O', click: () => focusedWindow()?.chooseFile() },
      { label: '儲存', accelerator: 'CmdOrCtrl+S', click: () => focusedWindow()?.send('save-requested') },
      { label: '重新載入文件', accelerator: 'CmdOrCtrl+R', click: () => focusedWindow()?.reload() },
      { label: '列印 / 儲存 PDF…', accelerator: 'CmdOrCtrl+P', click: () => focusedWindow()?.send('print-requested') },
      { type: 'separator' }, { role: 'close', label: '關閉視窗' }, { role: 'quit', label: '結束所有視窗' }
    ] },
    { label: '編輯', submenu: [
      { role: 'undo', label: '復原' }, { role: 'redo', label: '重做' }, { type: 'separator' },
      { role: 'cut', label: '剪下' }, { role: 'copy', label: '複製' }, { role: 'paste', label: '貼上' }, { role: 'selectAll', label: '全選' },
      { label: '搜尋', accelerator: 'CmdOrCtrl+F', click: () => focusedWindow()?.send('focus-search') }
    ] },
    { label: '檢視', submenu: [{ role: 'togglefullscreen', label: '全螢幕' }, { label: '切換主題', accelerator: 'CmdOrCtrl+Shift+L', click: () => focusedWindow()?.send('toggle-theme') }] },
    { label: 'Windows 整合', submenu: [
      { label: '加入檔案右鍵選單', click: () => configureIntegration(true) },
      { label: '移除檔案右鍵選單', click: () => configureIntegration(false) }
    ] }
  ]));
}
async function configureIntegration(enabled) {
  if (integrationBusy) return;
  integrationBusy = true;
  const window = BrowserWindow.getFocusedWindow();
  try {
    await setWindowsIntegration(getExecutablePath(app), enabled);
    const options = { type: 'info', title: 'Windows 整合', message: enabled ? '已加入「使用 MD Reader 開啟」。' : '已移除 MD Reader 的右鍵選單與開啟方式登錄。', detail: enabled ? '在 Markdown 檔案上按右鍵即可使用，也可從「開啟方式」選擇 MD Reader。Windows 11 可能需要先點「顯示更多選項」。搬移 EXE 或更新版本後，請重新執行此設定。' : '之後仍可從閱讀器內開啟文件。', buttons: ['確定'] };
    if (window && !window.isDestroyed()) await dialog.showMessageBox(window, options); else await dialog.showMessageBox(options);
  } catch (error) {
    const options = { type: 'error', title: 'Windows 整合未完成', message: '無法更新檔案右鍵選單。', detail: error.message, buttons: ['確定'] };
    if (window && !window.isDestroyed()) await dialog.showMessageBox(window, options); else await dialog.showMessageBox(options);
  } finally { integrationBusy = false; }
}
function createWindow(file) {
  let activeDocument = null;
  let documentId = 0;
  let dirty = false;
  let busy = false;
  let initialized = false;
  let pagination = { page: 1, total: 1 };
  const startupFile = file || settings.lastFile;
  const win = new BrowserWindow({ width: 1260, height: 850, minWidth: 760, minHeight: 540, backgroundColor: '#f7f8fa', autoHideMenuBar: true, title: 'MD Reader', icon: path.join(__dirname, 'assets', 'icon.png'), webPreferences: { preload: path.join(__dirname, 'preload.cjs'), contextIsolation: true, nodeIntegration: false, sandbox: true } });
  const send = (channel, data) => { if (!win.isDestroyed()) win.webContents.send(channel, data); };
  function confirmDiscard() {
    return !dirty || dialog.showMessageBoxSync(win, {
      type: 'warning', title: '未儲存的變更', message: '這份文件有未儲存的變更。',
      buttons: ['繼續編輯', '捨棄變更'], defaultId: 0, cancelId: 0, noLink: true
    }) === 1;
  }
  async function runFileAction(action) {
    if (busy || win.isDestroyed()) return false;
    busy = true;
    try { return await action(); }
    finally { busy = false; }
  }
  async function openDocument(file) {
    try {
      const document = await readDocument(file);
      if (win.isDestroyed() || !confirmDiscard()) return false;
      const { text, ...metadata } = document;
      activeDocument = metadata;
      dirty = false;
      settings.lastFile = document.path;
      settings.recent = [document.path, ...settings.recent.filter(item => item !== document.path)].slice(0, 12);
      saveSettings();
      win.setTitle(`${document.name} — MD Reader`);
      send('document', { id: ++documentId, path: document.path, name: document.name, text, size: document.size });
      send('settings', publicSettings());
      return true;
    } catch (error) {
      const message = error.code === 'ENOENT' ? '找不到這份文件，檔案可能已移動或刪除。' : error.code === 'EACCES' || error.code === 'EPERM' ? '無法存取文件，請確認檔案的讀取權限。' : error.message;
      send('notice', message); return false;
    }
  }
  const openFile = file => typeof file === 'string' && runFileAction(() => openDocument(file));
  const chooseFile = () => runFileAction(async () => {
    const result = await dialog.showOpenDialog(win, { title: '開啟 Markdown 文件', filters: [{ name: 'Markdown 與文字檔', extensions: ['md', 'markdown', 'mdown', 'mkd', 'txt'] }], properties: ['openFile'] });
    if (!result.canceled && !win.isDestroyed()) return openDocument(result.filePaths[0]);
    return false;
  });
  const save = (id, text) => runFileAction(async () => {
    if (!activeDocument || id !== documentId || typeof text !== 'string') return false;
    const file = activeDocument.path;
    const key = process.platform === 'win32' ? file.toLowerCase() : file;
    if (savingPaths.has(key)) { send('notice', '另一個視窗正在儲存這份文件，請稍後再試。'); return false; }
    savingPaths.add(key);
    try {
      if (activeDocument.encoding === 'big5') {
        const { response } = await dialog.showMessageBox(win, {
          type: 'warning', title: '轉換檔案編碼', message: '儲存時會將 Big5 文件轉為 UTF-8。',
          buttons: ['取消', '以 UTF-8 儲存'], defaultId: 0, cancelId: 0, noLink: true
        });
        if (response !== 1) return false;
      }
      Object.assign(activeDocument, await saveDocument(activeDocument, text));
      return true;
    } catch (error) {
      send('notice', error.code ? '無法儲存文件，請確認檔案仍存在且有寫入權限。' : error.message);
      return false;
    } finally { savingPaths.delete(key); }
  });
  const context = {
    send, chooseFile, openFile, save,
    initialize: () => {
      if (!initialized) {
        initialized = true;
        if (startupFile) openFile(startupFile);
      }
      return publicSettings();
    },
    newWindow: () => { createWindow(); return true; },
    reload: () => activeDocument && openFile(activeDocument.path),
    image: source => activeDocument ? readLocalImage(activeDocument.path, source).catch(() => null) : null,
    preferences: prefs => {
      if (prefs.theme === 'light' || prefs.theme === 'dark') settings.theme = prefs.theme;
      if (Number.isFinite(prefs.fontSize)) settings.fontSize = Math.max(13, Math.min(24, prefs.fontSize));
      if (Number.isFinite(prefs.pageWidth)) settings.pageWidth = prefs.pageWidth === 0 ? 0 : Math.max(600, Math.min(1600, Math.round(prefs.pageWidth / 20) * 20));
      if (Number.isFinite(prefs.sidebarWidth)) settings.sidebarWidth = Math.max(200, Math.min(600, Math.round(prefs.sidebarWidth)));
      saveSettings();
    },
    openLink: async href => {
      if (typeof href !== 'string' || !activeDocument) return;
      if (/^https?:\/\//i.test(href) || /^mailto:/i.test(href)) return shell.openExternal(href);
      if (/^[a-z][a-z\d+.-]*:/i.test(href) || /^[\\/]{2}/.test(href)) return;
      const target = path.resolve(path.dirname(activeDocument.path), decodeURIComponent(href.split(/[?#]/)[0]));
      if (isDocument(target)) return openFile(target);
    },
    pagination: state => { pagination = { page: Math.max(1, Number(state.page) || 1), total: Math.max(1, Number(state.total) || 1) }; },
    print: async () => {
      if (pagination.total > 1) {
        const result = await dialog.showMessageBox(win, { type: 'info', title: '列印目前頁', message: `這份文件採分頁閱讀，將列印第 ${pagination.page} 頁（共 ${pagination.total} 頁）。`, detail: '只列印目前顯示的部分，以避免一次展開整份大型文件占用大量記憶體。', buttons: ['列印目前頁', '取消'], defaultId: 0, cancelId: 1 });
        if (result.response !== 0 || win.isDestroyed()) return;
      }
      win.webContents.print({ printBackground: true });
    }
  };
  const id = win.webContents.id;
  context.setDirty = (document, value) => { if (document === documentId) dirty = value === true; };
  windows.set(id, context);
  win.on('close', event => { if (busy || !confirmDiscard()) event.preventDefault(); });
  win.on('closed', () => windows.delete(id));
  win.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  win.webContents.on('will-navigate', event => event.preventDefault());
  win.webContents.on('will-attach-webview', event => event.preventDefault());
  win.loadFile(path.join(__dirname, 'index.html'));
}
for (const [channel, method] of Object.entries({
  'initialize': 'initialize', 'choose-file': 'chooseFile', 'open-file': 'openFile',
  'new-window': 'newWindow', 'reload-document': 'reload', 'local-image': 'image',
  'save-document': 'save',
  'save-preferences': 'preferences', 'open-link': 'openLink', 'print': 'print', 'pagination': 'pagination'
})) ipcMain.handle(channel, (event, ...args) => windows.get(event.sender.id)?.[method](...args));
ipcMain.on('document-dirty', (event, id, dirty) => windows.get(event.sender.id)?.setDirty(id, dirty));
app.on('window-all-closed', () => app.quit());
