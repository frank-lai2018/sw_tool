const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const { randomUUID } = require('node:crypto');
const { promisify } = require('node:util');
const { execFile } = require('node:child_process');
const { getExecutablePath, setWindowsIntegration } = require('../src/windows-integration.cjs');
const run = promisify(execFile);
const powershell = path.join(process.env.SystemRoot || 'C:\\Windows', 'System32/WindowsPowerShell/v1.0/powershell.exe');
async function script(source, data) {
  const result = await run(powershell, ['-NoProfile', '-NonInteractive', '-EncodedCommand', Buffer.from('[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)\n$request = $env:MD_TEST_DATA | ConvertFrom-Json\n' + source, 'utf16le').toString('base64')], { windowsHide: true, env: { ...process.env, MD_TEST_DATA: JSON.stringify(data) } });
  return result.stdout.trim() ? JSON.parse(result.stdout.trim()) : null;
}
test('registers and removes only owned shell entries, quoting paths and preserving defaults', { skip: process.platform !== 'win32' }, async t => {
  const temp = await fs.mkdtemp(path.join(os.tmpdir(), 'md-integration-'));
  const branch = `Software\\MDReaderIntegrationTest\\${randomUUID()}`;
  const root = `${branch}\\Classes`;
  t.after(async () => {
    await script('[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree([string]$request.branch,$false)', { branch });
    await fs.rm(temp, { recursive: true, force: true });
  });
  const exe = path.join(temp, "reader 中文 & ' space.exe");
  const next = path.join(temp, 'updated.exe');
  await fs.writeFile(exe, 'fixture'); await fs.writeFile(next, 'fixture');
  const fixtureApp = { isPackaged: true, getPath: () => next };
  assert.equal(getExecutablePath(fixtureApp, { PORTABLE_EXECUTABLE_FILE: exe }), exe);
  assert.equal(getExecutablePath(fixtureApp, {}), next);
  assert.throws(() => getExecutablePath({ isPackaged: false }), /EXE/);
  await script('$key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($request.root+"\\.md");$key.SetValue("","Existing.Reader");$key.Dispose();$key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($request.root+"\\.md\\OpenWithProgids");$key.SetValue("Existing.Reader","");$key.Dispose()', { root });
  const status = await setWindowsIntegration(exe, true, { root }); assert.equal(status.enabled, true);
  async function inspect() {
    return script('$key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($request.root);function Read-Value($p,$v){$k=$key.OpenSubKey($p);if($k){try{return $k.GetValue($v)}finally{$k.Dispose()}};return $null};@{command=(Read-Value "SystemFileAssociations\\.md\\shell\\MDReader\\command" "");verb=(Read-Value "SystemFileAssociations\\.md\\shell\\MDReader" "");appCommand=(Read-Value "Applications\\MDReader.exe\\shell\\open\\command" "");default=(Read-Value ".md" "");progIds=@($key.OpenSubKey(".md\\OpenWithProgids").GetValueNames())} | ConvertTo-Json -Compress;$key.Dispose()', { root });
  }
  let state = await inspect();
  assert.equal(state.command, `"${exe}" "%1"`); assert.equal(state.appCommand, state.command);
  assert.equal(state.verb, '使用 MD Reader 開啟'); assert.equal(state.default, 'Existing.Reader');
  assert.ok(state.progIds.includes('MDReader.Markdown')); assert.ok(state.progIds.includes('Existing.Reader'));
  await setWindowsIntegration(next, true, { root }); state = await inspect();
  assert.equal(state.command, `"${next}" "%1"`);
  await setWindowsIntegration(next, false, { root }); state = await inspect();
  assert.equal(state.command, null); assert.equal(state.appCommand, null);
  assert.equal(state.default, 'Existing.Reader'); assert.deepEqual(state.progIds, ['Existing.Reader']);
  await setWindowsIntegration(next, false, { root });
  await assert.rejects(setWindowsIntegration(exe, true, { root: 'Software\\Unrelated' }), /無效/);
});
