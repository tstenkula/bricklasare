#requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$staging = Join-Path ([IO.Path]::GetTempPath()) ('bricklasare-inspection-package-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($staging)
try {
    # Explicit allow-list; no recursive copy of a working installation or reader data.
    foreach ($name in @('Samla-ljudunderlag.cmd','Samla-ljudunderlag.ps1','LAS-MIG.txt','ReaderWebFiles.cs','ReaderSoundInspection.cs')) {
        [IO.File]::Copy((Join-Path $root ('tools/reader-ui-inspection/' + $name)), (Join-Path $staging $name))
    }
    [IO.File]::Copy((Join-Path $root 'Program/ReaderNetwork.cs'), (Join-Path $staging 'ReaderNetwork.cs'))
    [IO.File]::Copy((Join-Path $root 'LICENSE.txt'), (Join-Path $staging 'LICENSE.txt'))
    $manifest = Get-ChildItem -LiteralPath $staging -File | Sort-Object Name | ForEach-Object {
        (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash + '  ' + $_.Name
    }
    [IO.File]::WriteAllLines((Join-Path $staging 'SHA256SUMS.txt'), $manifest, (New-Object Text.UTF8Encoding($false)))
    $dist = Join-Path $root 'dist'; [void][IO.Directory]::CreateDirectory($dist)
    $zip = Join-Path $dist 'Bricklasaren-Gen1-ljudunderlag-4-Windows.zip'
    if (Test-Path -LiteralPath $zip) { [IO.File]::Delete($zip) }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($staging, $zip)
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $expected = @('Samla-ljudunderlag.cmd','Samla-ljudunderlag.ps1','LAS-MIG.txt','ReaderWebFiles.cs','ReaderSoundInspection.cs','ReaderNetwork.cs','LICENSE.txt','SHA256SUMS.txt') | Sort-Object
        $actual = $archive.Entries.FullName | Sort-Object
        if (($expected -join '|') -cne ($actual -join '|')) { throw 'Unexpected inspection package files.' }
    } finally { $archive.Dispose() }
    Get-FileHash -LiteralPath $zip -Algorithm SHA256 | Format-List
} finally { [IO.Directory]::Delete($staging, $true) }
