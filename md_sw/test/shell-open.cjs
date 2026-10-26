// Explicit integration check: registers this build for the CURRENT Windows user.
// This is intentionally not part of npm test; it leaves the requested registration installed.
const { _electron } = require('playwright-core');
const { execFile } = require('node:child_process');
const { promisify } = require('node:util');
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const assert = require('node:assert/strict');
const run = promisify(execFile);
const powershell = path.join(process.env.SystemRoot || 'C:\\Windows', 'System32/WindowsPowerShell/v1.0/powershell.exe');
async function ps(source, data = {}) {
  const code = '[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)\n$ErrorActionPreference="Stop"\n$request=$env:MD_SHELL_TEST|ConvertFrom-Json\n' + source;
  const result = await run(powershell, ['-NoProfile', '-NonInteractive', '-EncodedCommand', Buffer.from(code, 'utf16le').toString('base64')], { windowsHide: true, env: { ...process.env, MD_SHELL_TEST: JSON.stringify(data) } });
  return result.stdout.trim() ? JSON.parse(result.stdout.trim()) : null;
}
const readRegistration = () => ps(`
function Read-Text($p,$name){$k=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($p);if($k){try{return [string]$k.GetValue($name)}finally{$k.Dispose()}};return ''}
@{command=(Read-Text 'Software\\Classes\\SystemFileAssociations\\.md\\shell\\MDReader\\command' '');default=(Read-Text 'Software\\Classes\\.md' '');userChoice=(Read-Text 'Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\FileExts\\.md\\UserChoice' 'ProgId')}|ConvertTo-Json -Compress
`);
(async () => {
  const root = path.resolve(__dirname, '..');
  const executable = path.join(root, 'dist', `MD-Reader-${require('../package.json').version}-Windows.exe`);
  const file = path.join(root, 'dist', '使用說明.md');
  await fs.access(executable); await fs.access(file);
  const before = await readRegistration();
  const temp = await fs.mkdtemp(path.join(os.tmpdir(), 'md-shell-menu-'));
  let app, launchedProcess;
  try {
    app = await _electron.launch({ executablePath: path.join(root, 'dist/win-unpacked/MD Reader.exe'), args: [`--user-data-dir=${temp}`, file], env: { ...process.env, PORTABLE_EXECUTABLE_FILE: executable } });
    const page = await app.firstWindow(); await page.waitForSelector('#content h1');
    const result = await app.evaluate(async ({ Menu, dialog }) => new Promise(resolve => {
      dialog.showMessageBox = async (...args) => { resolve(args.at(-1)); return { response: 0 }; };
      Menu.getApplicationMenu().items.find(item => item.label === 'Windows 整合').submenu.items.find(item => item.label === '加入檔案右鍵選單').click();
    }));
    assert.equal(result.type, 'info'); assert.match(result.message, /已加入/);
    await app.close(); app = null;
    const after = await readRegistration();
    assert.equal(after.command, `"${executable}" "%1"`);
    assert.equal(after.default, before.default); assert.equal(after.userChoice, before.userChoice);
    const launched = await ps('$start=New-Object System.Diagnostics.ProcessStartInfo;$start.FileName=$request.file;$start.Verb="MDReader";$start.UseShellExecute=$true;$p=[System.Diagnostics.Process]::Start($start);@{pid=$p.Id}|ConvertTo-Json -Compress', { file });
    const deadline = Date.now() + 45000;
    while (Date.now() < deadline) {
      const state = await ps('$p=Get-CimInstance Win32_Process -Filter ("ParentProcessId = "+[int]$request.pid)|Where-Object{$_.Name -eq "MD Reader.exe" -and $_.CommandLine -notmatch "--type="}|Select-Object -First 1;if($p){$w=Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue;@{pid=$p.ProcessId;title=$w.MainWindowTitle}|ConvertTo-Json -Compress}else{@{pid=0;title=""}|ConvertTo-Json -Compress}', launched);
      if (state.pid) launchedProcess = state.pid;
      if (state.title === '使用說明.md — MD Reader') break;
      await new Promise(resolve => setTimeout(resolve, 500));
    }
    assert.ok(launchedProcess, 'Shell verb did not launch the reader');
    const actual = await ps('$p=Get-Process -Id $request.pid;@{title=$p.MainWindowTitle}|ConvertTo-Json -Compress', { pid: launchedProcess });
    assert.equal(actual.title, '使用說明.md — MD Reader');
    console.log('PASS: packaged menu registers stable portable EXE, preserves defaults, and Windows ShellExecute opens a Unicode Markdown document using the MDReader right-click verb.');
  } finally {
    if (app) await app.close();
    if (launchedProcess) await ps('$p=Get-Process -Id $request.pid -ErrorAction SilentlyContinue;if($p){[void]$p.CloseMainWindow()}', { pid: launchedProcess });
    await fs.rm(temp, { recursive: true, force: true });
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
