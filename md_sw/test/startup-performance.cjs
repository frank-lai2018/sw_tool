// Native launch benchmark without DevTools, using an isolated profile.
const { execFile } = require('node:child_process');
const { promisify } = require('node:util');
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
(async () => {
  const root = path.resolve(__dirname, '..');
  const executable = process.argv.find(arg => arg.startsWith('--exe='))?.slice(6) || path.join(root, 'dist/win-unpacked/MD Reader.exe');
  const temp = await fs.mkdtemp(path.join(os.tmpdir(), 'md-startup-'));
  const file = path.join(temp, '啟動測試.md');
  await fs.writeFile(file, '# 啟動測試\n\n測試閱讀器啟動速度。');
  const script = `
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)
$r=$env:MD_STARTUP_TEST|ConvertFrom-Json
$start=New-Object System.Diagnostics.ProcessStartInfo
$start.FileName=$r.executable
$start.Arguments='--user-data-dir="'+$r.profile+'" "'+$r.file+'"'
$start.UseShellExecute=$false
$timer=[Diagnostics.Stopwatch]::StartNew()
$launcher=[Diagnostics.Process]::Start($start)
$reader=$null
try {
  while($timer.Elapsed.TotalSeconds -lt 60){
    $candidates=Get-CimInstance Win32_Process -Filter "Name='MD Reader.exe'"|Where-Object{$_.CommandLine -and $_.CommandLine.Contains($r.profile) -and $_.CommandLine -notmatch '--type='}
    foreach($candidate in $candidates){
      $p=Get-Process -Id $candidate.ProcessId -ErrorAction SilentlyContinue
      if($p -and $p.MainWindowTitle -eq '啟動測試.md — MD Reader'){$reader=$p;break}
    }
    if($reader){break}
    Start-Sleep -Milliseconds 100
  }
  if(-not $reader){throw 'Reader did not open the benchmark file.'}
  @{executable=$r.executable;openMs=$timer.ElapsedMilliseconds}|ConvertTo-Json -Compress
}finally{
  if($reader){[void]$reader.CloseMainWindow();[void]$reader.WaitForExit(10000)}
  if(-not $launcher.WaitForExit(15000)){$launcher.Kill()}
}
`;
  try {
    const result = await promisify(execFile)(path.join(process.env.SystemRoot, 'System32/WindowsPowerShell/v1.0/powershell.exe'), ['-NoProfile', '-NonInteractive', '-EncodedCommand', Buffer.from(script, 'utf16le').toString('base64')], {
      windowsHide: true, timeout: 90000, env: { ...process.env, MD_STARTUP_TEST: JSON.stringify({ executable, file, profile: path.join(temp, 'profile') }) }
    });
    console.log(result.stdout.trim());
  } finally { await fs.rm(temp, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 }); }
})().catch(error => { console.error(error.stderr || error.message); process.exitCode = 1; });
