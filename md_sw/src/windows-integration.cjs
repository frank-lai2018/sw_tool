const fs = require('node:fs');
const path = require('node:path');
const { execFile } = require('node:child_process');
const { promisify } = require('node:util');
const run = promisify(execFile);

function getExecutablePath(app, env = process.env) {
  if (!app.isPackaged) throw new Error('請使用打包完成的 Windows EXE 設定右鍵選單。');
  const executable = env.PORTABLE_EXECUTABLE_FILE || app.getPath('exe');
  if (!path.isAbsolute(executable) || path.extname(executable).toLowerCase() !== '.exe' || !fs.existsSync(executable)) throw new Error('找不到可註冊的 EXE，請將閱讀器放在固定資料夾後重新執行。');
  return executable;
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
module.exports = { getExecutablePath, setWindowsIntegration };
