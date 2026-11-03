#requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
# Read the application's explicit package allow-list, not a recursive copy of the workspace.
$source = [IO.File]::ReadAllText((Join-Path $root 'Program\ReceptionSupport.cs'))
$start = $source.IndexOf('return new string[] {')
$end = $source.IndexOf('};', $start)
$block = $source.Substring($start, $end - $start)
$files = @('Starta-Bricklasaren.cmd') + @([regex]::Matches($block, '"([^"\r\n]+)"') | ForEach-Object { $_.Groups[1].Value })
$stage = Join-Path ([IO.Path]::GetTempPath()) ('bricklasare-package-' + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $stage | Out-Null
    foreach ($file in $files) {
        if ($file.Contains('..') -or [IO.Path]::IsPathRooted($file)) { throw 'Unsafe package path' }
        $destination = Join-Path $stage $file
        [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
        Copy-Item -LiteralPath (Join-Path $root $file) -Destination $destination
    }
    $dist = Join-Path $root 'dist'; [IO.Directory]::CreateDirectory($dist) | Out-Null
    $zip = Join-Path $dist 'Bricklasaren-3.5.1-Windows.zip'
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    # .NET Framework CreateFromDirectory emits backslashes on Windows. ZIP paths
    # must use forward slashes to pass the same strict updater checks everywhere.
    $archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $stage $file), $file.Replace('\', '/'), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
    Get-FileHash -LiteralPath $zip -Algorithm SHA256
} finally { if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force } }
