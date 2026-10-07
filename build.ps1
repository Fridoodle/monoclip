$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$sdk = Join-Path $env:LOCALAPPDATA 'MonoClipBuild/dotnet/dotnet.exe'
if (!(Test-Path $sdk)) { $sdk = 'dotnet' }
& python tools/prepare-runtime.py
if ($LASTEXITCODE) { throw 'OBS preparation failed' }
& $sdk run --project tests/MonoClip.Tests
if ($LASTEXITCODE) { throw 'Core tests failed' }
& $sdk run --project tests/MonoClip.UiTests
if ($LASTEXITCODE) { throw 'UI tests failed' }
& $sdk publish src/MonoClip.Windows -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o dist/MonoClip
if ($LASTEXITCODE) { throw 'Publish failed' }
Write-Host 'App: dist/MonoClip/MonoClip.exe'
