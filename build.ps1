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
if (Test-Path dist/MonoClip) { Remove-Item -Recurse -Force dist/MonoClip }
# The app runs from app\ (OBS finds its helpers next to the running EXE); the root holds only the starter.
& $sdk publish src/MonoClip.Windows -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o dist/MonoClip/app
if ($LASTEXITCODE) { throw 'Publish failed' }
# NativeAOT starter (~1.3 MB) needs the MSVC linker (Visual Studio Build Tools); otherwise the same
# code is published as a trimmed single-file EXE (~10.7 MB).
& $sdk publish src/MonoClip.Launcher -c Release -p:PublishAot=true -o dist/MonoClip 2>$null
if ($LASTEXITCODE) {
    Write-Host 'NativeAOT not available (no MSVC linker); building the single-file starter instead.'
    & $sdk publish src/MonoClip.Launcher -c Release -o dist/MonoClip
    if ($LASTEXITCODE) { throw 'Starter publish failed' }
}
Get-ChildItem dist/MonoClip -File | Where-Object { $_.Name -ne 'MonoClip.exe' } | Remove-Item
& python tools/package-portable.py --app dist/MonoClip
if ($LASTEXITCODE) { throw 'Packaging failed' }
Write-Host 'App:      dist/MonoClip/MonoClip.exe (starter) -> dist/MonoClip/app'
Write-Host 'Download: dist/MonoClip-*-win-x64-portable.zip'
