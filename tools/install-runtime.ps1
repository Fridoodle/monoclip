param(
    [string]$Destination = $PSScriptRoot,
    [string]$ArchivePath
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$version = '32.2.2'
$expected = '4d6e40e3ab155f56b30de517380566a206d74b63cdf5ad49aa596924768f97e1'
$url = "https://github.com/obsproject/obs-studio/releases/download/$version/OBS-Studio-$version-Windows-x64.zip"
$root = [IO.Path]::GetFullPath($Destination)
if (Get-Process -Name MonoClip -ErrorAction SilentlyContinue) {
    throw 'MonoClip zuerst im Tray beenden, dann die Runtime einrichten.'
}
New-Item -ItemType Directory -Force -Path $root | Out-Null
$temp = Join-Path ([IO.Path]::GetTempPath()) ('MonoClip-runtime-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
$archive = $null
try {
    if ($ArchivePath) {
        $zipPath = [IO.Path]::GetFullPath($ArchivePath)
    } else {
        $zipPath = Join-Path $temp 'obs.zip'
        Write-Host "Lade offizielle OBS-$version-Runtime..."
        Invoke-WebRequest -Uri $url -OutFile $zipPath
    }
    # Use .NET directly: a parent PowerShell 7 process can leave Windows PowerShell's
    # module path without the module exporting Get-FileHash.
    $sha = [Security.Cryptography.SHA256]::Create()
    $hashInput = [IO.File]::OpenRead($zipPath)
    try { $actual = [BitConverter]::ToString($sha.ComputeHash($hashInput)).Replace('-', '').ToLowerInvariant() }
    finally { $hashInput.Dispose(); $sha.Dispose() }
    if ($actual -ne $expected) { throw 'SHA256 stimmt nicht. Es wurden keine Runtime-Dateien installiert.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    $modules = @('win-capture','win-wasapi','obs-ffmpeg','obs-outputs','obs-nvenc')
    $count = 0
    foreach ($entry in $archive.Entries) {
        $name = $entry.FullName
        if (!$entry.Name -or $name.EndsWith('.pdb')) { continue }
        $relative = $null
        if ($name.StartsWith('bin/64bit/')) {
            $lower = $name.ToLowerInvariant()
            if ($lower -match 'qt6|obs64\.exe|obspython|obslua|python|imageformats/|platforms/|styles/|sqldrivers/|tls/') { continue }
            $tail = $name.Substring('bin/64bit/'.Length)
            if ($tail.Contains('/')) { continue }
            $relative = $tail
        } elseif ($name.StartsWith('data/libobs/')) {
            $relative = 'runtime/' + $name
        } else {
            foreach ($module in $modules) {
                if ($name.StartsWith("data/obs-plugins/$module/") -or $name -eq "obs-plugins/64bit/$module.dll") {
                    $relative = 'runtime/' + $name
                    break
                }
            }
        }
        if (!$relative) { continue }
        $target = [IO.Path]::GetFullPath((Join-Path $root $relative))
        if (!$target.StartsWith($root.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Unsicherer Archivpfad.'
        }
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        $input = $entry.Open()
        try {
            $output = [IO.File]::Create($target)
            try { $input.CopyTo($output) } finally { $output.Dispose() }
        } finally { $input.Dispose() }
        $count++
    }
    $required = @('obs.dll','obs-ffmpeg-mux.exe','libobs-d3d11.dll','runtime/data/libobs/default.effect','runtime/obs-plugins/64bit/win-capture.dll')
    foreach ($file in $required) {
        if (!(Test-Path (Join-Path $root $file))) { throw "Runtime unvollstaendig: $file" }
    }
    @{version=$version;url=$url;sha256=$actual;files=$count;source='official OBS release downloaded by user'} | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $root 'OBS-ORIGIN.json')
    Write-Host "Runtime eingerichtet: $count Dateien. Jetzt MonoClip.exe starten."
} finally {
    if ($archive) { $archive.Dispose() }
    Remove-Item -Recurse -Force -Path $temp -ErrorAction SilentlyContinue
}
