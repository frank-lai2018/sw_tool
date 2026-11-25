const fs = require('node:fs');
const path = require('node:path');
const { execFile } = require('node:child_process');
const { promisify } = require('node:util');
const { createHash, randomUUID } = require('node:crypto');
const run = promisify(execFile);

function getExecutablePath(app, env = process.env) {
  if (!app.isPackaged) throw new Error('請使用打包完成的 Windows EXE 設定右鍵選單。');
  const executable = env.PORTABLE_EXECUTABLE_FILE || app.getPath('exe');
  if (!path.isAbsolute(executable) || path.extname(executable).toLowerCase() !== '.exe' || !fs.existsSync(executable)) throw new Error('找不到可註冊的 EXE，請將閱讀器放在固定資料夾後重新執行。');
  return executable;
}
async function prepareIntegrationExecutable(app, env = process.env) {
  // Electron's fs treats app.asar as a virtual directory. Copy the physical
  // archive and runtime using the unpatched filesystem implementation.
  const disk = process.versions.electron ? require('original-fs') : fs;
  const executable = getExecutablePath(app, env);
  if (!env.PORTABLE_EXECUTABLE_FILE) return executable;
  // A portable launcher extracts Electron on every launch. Keep a complete,
  // versioned runtime for shell opening instead of registering its temp EXE.
  const source = path.dirname(app.getPath('exe'));
  const digest = createHash('sha256');
  const archive = path.join(source, 'resources', 'app.asar');
  if (disk.existsSync(archive)) digest.update(await disk.promises.readFile(archive));
  else {
    const application = path.join(source, 'resources', 'app');
    async function hashDirectory(directory) {
      const entries = await disk.promises.readdir(directory, { withFileTypes: true });
      for (const entry of entries.sort((a, b) => a.name.localeCompare(b.name))) {
        const file = path.join(directory, entry.name);
        if (entry.isDirectory()) await hashDirectory(file);
        else digest.update(JSON.stringify([path.relative(application, file), createHash('sha256').update(await disk.promises.readFile(file)).digest('hex')]));
      }
    }
    await hashDirectory(application);
  }
  const revision = digest.digest('hex').slice(0, 16);
  const base = path.join(env.LOCALAPPDATA || app.getPath('appData'), 'MDReader', 'runtime');
  const destination = path.join(base, `${app.getVersion()}-${revision}`);
  const target = path.join(destination, path.basename(app.getPath('exe')));
  if (disk.existsSync(target)) return target;
  await disk.promises.mkdir(base, { recursive: true });
  const staging = path.join(base, `.prepare-${randomUUID()}`);
  try {
    await disk.promises.cp(source, staging, { recursive: true, errorOnExist: true, force: false });
    try { await disk.promises.rename(staging, destination); }
    catch (error) { if (!disk.existsSync(target)) throw error; }
  } finally { await disk.promises.rm(staging, { recursive: true, force: true }); }
  return target;
}
async function setWindowsIntegration(executable, enabled, options = {}) {
  if (process.platform !== 'win32') throw new Error('右鍵選單整合只適用 Windows。');
  if (enabled && (!path.isAbsolute(executable) || path.extname(executable).toLowerCase() !== '.exe' || !fs.existsSync(executable))) throw new Error('指定的 Windows EXE 不存在。');
  const root = options.root || 'Software\\Classes';
  if (root !== 'Software\\Classes' && !/^Software\\MDReaderIntegrationTest\\[a-z\d-]+\\Classes$/i.test(root)) throw new Error('無效的登錄位置。');
  const script = fs.readFileSync(path.join(__dirname, 'windows-integration.ps1'), 'utf8');
  const powershell = path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe');
  const { stdout } = await run(powershell, ['-NoProfile', '-NonInteractive', '-EncodedCommand', Buffer.from(script, 'utf16le').toString('base64')], {
    windowsHide: true, timeout: 30000, maxBuffer: 1024 * 1024,
    env: { ...process.env, MD_READER_INTEGRATION: JSON.stringify({ executable, enabled: Boolean(enabled), root }) }
  });
  return JSON.parse(stdout.trim().replace(/^\uFEFF/, ''));
}
module.exports = { getExecutablePath, prepareIntegrationExecutable, setWindowsIntegration };
