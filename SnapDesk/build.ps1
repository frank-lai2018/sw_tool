$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'SnapDesk.csproj'
$projectVersion = ([xml](Get-Content -LiteralPath $projectPath -Raw)).Project.PropertyGroup.Version
$publishPath = Join-Path $PSScriptRoot "publish\$projectVersion"
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $publishPath
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$exePath = Join-Path $publishPath 'SnapDesk.exe'
$testProcess = Start-Process -FilePath $exePath -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
if ($testProcess.ExitCode -ne 0) { throw "Self-test failed. See $publishPath\test-results.txt" }
$deliveryPath = Join-Path (Split-Path $PSScriptRoot -Parent) "SnapDesk-$projectVersion-Windows-x64.exe"
Copy-Item -LiteralPath $exePath -Destination $deliveryPath -Force
Write-Output "Ready: $deliveryPath"
