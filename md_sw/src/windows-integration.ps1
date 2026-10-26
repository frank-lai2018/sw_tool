$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$request = $env:MD_READER_INTEGRATION | ConvertFrom-Json
$registryRoot = [string]$request.root
if ($registryRoot -ne 'Software\Classes' -and $registryRoot -notmatch '^Software\\MDReaderIntegrationTest\\[a-z\d-]+\\Classes$') { throw 'Invalid registry location.' }
$exe = [string]$request.executable
$progId = 'MDReader.Markdown'
$application = 'Applications\MDReader.exe'
$extensions = @('.md', '.markdown', '.mdown', '.mkd')
$root = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryRoot)
function Set-Text([string]$subkey, [string]$name, [string]$value) {
    $key = $root.CreateSubKey($subkey)
    try { $key.SetValue($name, $value, [Microsoft.Win32.RegistryValueKind]::String) } finally { $key.Dispose() }
}
try {
    if ($request.enabled) {
        if (-not [System.IO.Path]::IsPathRooted($exe) -or -not [System.IO.File]::Exists($exe) -or [System.IO.Path]::GetExtension($exe) -ine '.exe') { throw 'EXE does not exist.' }
        $command = '"' + $exe + '" "%1"'
        $icon = '"' + $exe + '",0'
        Set-Text $progId '' 'Markdown Document (MD Reader)'
        Set-Text "$progId\DefaultIcon" '' $icon
        Set-Text "$progId\shell\open\command" '' $command
        Set-Text "$progId\Application" 'ApplicationName' 'MD Reader'
        Set-Text "$progId\Application" 'ApplicationIcon' $icon
        Set-Text $application 'FriendlyAppName' 'MD Reader'
        Set-Text "$application\DefaultIcon" '' $icon
        Set-Text "$application\shell\open\command" '' $command
        foreach ($extension in $extensions) {
            Set-Text "$extension\OpenWithProgids" $progId ''
            Set-Text "$application\SupportedTypes" $extension ''
            $verb = "SystemFileAssociations\$extension\shell\MDReader"
            Set-Text $verb '' '使用 MD Reader 開啟'
            Set-Text $verb 'Icon' $icon
            Set-Text $verb 'MultiSelectModel' 'Single'
            Set-Text "$verb\command" '' $command
        }
    } else {
        foreach ($extension in $extensions) {
            $root.DeleteSubKeyTree("SystemFileAssociations\$extension\shell\MDReader", $false)
            $key = $root.OpenSubKey("$extension\OpenWithProgids", $true)
            if ($key) { try { $key.DeleteValue($progId, $false) } finally { $key.Dispose() } }
        }
        $root.DeleteSubKeyTree($progId, $false)
        $root.DeleteSubKeyTree($application, $false)
    }
} finally { $root.Dispose() }
if ($registryRoot -eq 'Software\Classes') {
    Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class MDReaderShell { [DllImport("shell32.dll")] public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2); }'
    [MDReaderShell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
}
@{ enabled = [bool]$request.enabled; executable = $exe; extensions = $extensions } | ConvertTo-Json -Compress
