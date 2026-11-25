const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const { randomUUID } = require('node:crypto');
const { promisify } = require('node:util');
const { execFile } = require('node:child_process');
const { getExecutablePath, prepareIntegrationExecutable, setWindowsIntegration } = require('../src/windows-integration.cjs');
const run = promisify(execFile);
const powershell = path.join(process.env.SystemRoot || 'C:\\Windows', 'System32/WindowsPowerShell/v1.0/powershell.exe');
test('portable shell registration keeps a complete versioned runtime outside temporary extraction', async t => {
  const temp = await fs.mkdtemp(path.join(os.tmpdir(), 'md-fast-runtime-'));
  t.after(() => fs.rm(temp, { recursive: true, force: true }));
  const source = path.join(temp, 'extracted');
  await fs.mkdir(path.join(source, 'resources'), { recursive: true });
  const exe = path.join(source, 'MD Reader.exe');
  const portable = path.join(temp, 'portable.exe');
  await fs.writeFile(exe, 'runtime'); await fs.writeFile(portable, 'launcher');
  await fs.writeFile(path.join(source, 'resources/app.asar'), 'version one');
  await fs.writeFile(path.join(source, 'runtime.dll'), 'dependency');
  const app = { isPackaged: true, getPath: name => name === 'exe' ? exe : temp, getVersion: () => '1.3.2' };
  const env = { PORTABLE_EXECUTABLE_FILE: portable, LOCALAPPDATA: path.join(temp, 'local') };
  const cached = await prepareIntegrationExecutable(app, env);
  assert.notEqual(cached, exe); assert.notEqual(cached, portable);
  assert.equal(await fs.readFile(path.join(path.dirname(cached), 'runtime.dll'), 'utf8'), 'dependency');
  assert.equal(await prepareIntegrationExecutable(app, env), cached);
  await fs.writeFile(path.join(source, 'resources/app.asar'), 'version two');
  const updated = await prepareIntegrationExecutable(app, env);
  assert.notEqual(updated, cached);
  assert.equal(await fs.readFile(path.join(path.dirname(cached), 'resources/app.asar'), 'utf8'), 'version one');
  assert.equal(await fs.readFile(path.join(path.dirname(updated), 'resources/app.asar'), 'utf8'), 'version two');
  assert.equal(await prepareIntegrationExecutable(app, {}), exe);
  await fs.unlink(path.join(source, 'resources/app.asar'));
  await fs.mkdir(path.join(source, 'resources/app/src'), { recursive: true });
  await fs.writeFile(path.join(source, 'resources/app/src/main.cjs'), 'unpacked app');
  const unpacked = await prepareIntegrationExecutable(app, env);
  assert.notEqual(unpacked, updated);
  assert.equal(await fs.readFile(path.join(path.dirname(unpacked), 'resources/app/src/main.cjs'), 'utf8'), 'unpacked app');
  assert.ok((await fs.readdir(path.dirname(path.dirname(cached)))).every(name => !name.startsWith('.prepare-')));
});
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
