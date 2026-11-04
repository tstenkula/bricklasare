#requires -Version 5.1
param([Parameter(Mandatory=$true)][string]$Plan, [switch]$VerifyPackageOnly)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$CurrentVersion = '3.6.1'
$PublicKey = '<RSAKeyValue><Modulus>n8JTA0cbbRrraGNb11fX4Dyanoa8S9TzTBGl8Edfagx743HROUj9lDCwJvuVF6cNpH4mB2dN33O+XfYKZMrUaJ0K/3Q9gmPLqedlvbpuHWlEaeUzekdT4Npo7EZpwo+Cwcxjlmlahgoll1MkjEu8QERzAQEeuTB4vZCsKdXdYcRlKAQuPEKqCWkiH49prMDvVdqUUYLl2TPLcHPD8MDdkbFh/glWJDTRB1No++PCUU8PVG80X94BnMe1E/+LLyTjxaNYVSsIFZlD9XX3eup7t/UTKUk9RC1u8WY23l74ccXwy71lirntfOFW6MdK4kFsIGOk+2PF/QGwoIcpwfhSt7+Id0y85VGEqe002CngsfxUQ7BkAAAB2+D4vPcU3FBLkleYkG5B+WXasbCslTGFXG+hnFpTU2R5XQECRJ2UXjuJN/lDaw8AE7dwUTTfibfYGs9mAUZ6nECBsL3OmpXtCK9ZyTHx2AWs+hvXP+7hzcluf12K2H4MveyyNcOFSNed</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>'
$backup = $null
$mutex = $null
$locked = $false
$dataRoot = $null
$candidate = $null
$zip = $null
$memory = $null

function Read-UpdateFile([string]$Path, [long]$Maximum) {
    if (((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Linked update file' }
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        if ($stream.Length -lt 1 -or $stream.Length -gt $Maximum) { throw 'Invalid update file size' }
        $bytes = New-Object byte[] ([int]$stream.Length)
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $read = $stream.Read($bytes, $offset, $bytes.Length - $offset)
            if ($read -le 0) { throw 'Truncated update file' }
            $offset += $read
        }
        return ,$bytes
    } finally { $stream.Dispose() }
}
function Assert-PlainPath([string]$Path) {
    $part = [IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrEmpty($part)) {
        if (((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Linked update directory' }
        $part = [IO.Path]::GetDirectoryName($part.TrimEnd('\'))
    }
}
function Get-SignedAsset([byte[]]$Envelope, [string]$Channel) {
    if ($Channel -notin @('stable', 'preview')) { throw 'Invalid update channel' }
    $lines = [Text.Encoding]::UTF8.GetString($Envelope).TrimEnd([char[]]"`r`n").Split([char]"`n")
    if ($lines.Length -ne 3 -or $lines[0] -cne 'BRICKLASARE-UPDATE-1') { throw 'Invalid signed feed' }
    $payload = [Convert]::FromBase64String($lines[1])
    $signature = [Convert]::FromBase64String($lines[2])
    $rsa = New-Object Security.Cryptography.RSACryptoServiceProvider
    try {
        $rsa.PersistKeyInCsp = $false
        $rsa.FromXmlString($PublicKey)
        if (-not $rsa.VerifyData($payload, [Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'), $signature)) { throw 'Invalid update signature' }
    } finally { $rsa.Dispose() }
    $feed = [Text.Encoding]::UTF8.GetString($payload) | ConvertFrom-Json
    if ($feed.Schema -ne 1 -or $feed.Channel -cne $Channel -or $null -eq $feed.Windows) { throw 'Invalid signed package' }
    $asset = $feed.Windows
    if ($asset.Version -notmatch '\A[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}\z') { throw 'Invalid version' }
    $version = [Version]$asset.Version
    if ($version.Major -gt 65535 -or $version.Minor -gt 65535 -or $version.Build -gt 65535 -or $version -le [Version]$CurrentVersion) { throw 'Package must be newer than this installer' }
    $url = [Uri]$asset.Url
    if (-not $url.IsAbsoluteUri -or $url.Scheme -cne 'https' -or $url.Host -cne 'github.com' -or -not $url.IsDefaultPort -or $url.UserInfo.Length -ne 0 -or $url.Query.Length -ne 0 -or $url.Fragment.Length -ne 0 -or -not $url.AbsolutePath.StartsWith('/tstenkula/bricklasare/releases/download/', [StringComparison]::Ordinal) -or -not $url.AbsolutePath.EndsWith('.zip', [StringComparison]::Ordinal)) { throw 'Invalid signed download URL' }
    if ($asset.Size -lt 1 -or $asset.Size -gt 52428800 -or $asset.Sha256 -notmatch '\A[0-9a-fA-F]{64}\z') { throw 'Invalid signed package constraints' }
    return $asset
}

try {
    $planPath = [IO.Path]::GetFullPath($Plan)
    $jobRoot = Split-Path -Parent $planPath
    Assert-PlainPath $jobRoot
    $planBytes = Read-UpdateFile $planPath 16384
    $job = [Text.Encoding]::UTF8.GetString($planBytes).TrimStart([char]0xFEFF) | ConvertFrom-Json
    $envelope = Read-UpdateFile (Join-Path $jobRoot 'release.feed') 65536
    $asset = Get-SignedAsset $envelope $job.Channel
    $archive = Join-Path $jobRoot 'package.zip'
    # Authenticate a bounded in-memory snapshot; extraction never reopens this path.
    $bytes = Read-UpdateFile $archive 52428800
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '') } finally { $sha.Dispose() }
    if ($bytes.LongLength -ne $asset.Size -or $hash -ine $asset.Sha256) { throw 'Package verification failed' }
    # CI exercises this exact signature/hash path without installing or running package code.
    if ($VerifyPackageOnly) { Write-Host 'PASS: installer signature and package verification'; exit 0 }
    $expectedData = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Tobtech\Omnikey-RCO-v2'
    if ([IO.Path]::GetFullPath($job.Data).TrimEnd('\') -ine $expectedData.TrimEnd('\') -or (Split-Path -Parent $jobRoot) -ine (Join-Path $expectedData 'updates') -or (Split-Path -Leaf $jobRoot) -notmatch '\A[a-f0-9]{32}\z' -or $job.ParentId -le 1) { throw 'Invalid update plan location' }
    $dataRoot = $expectedData
    $target = [IO.Path]::GetFullPath($job.Target).TrimEnd('\')
    if ($target -eq [IO.Path]::GetPathRoot($target) -or -not (Test-Path -LiteralPath (Join-Path $target 'Program\Entry.cs') -PathType Leaf)) { throw 'Invalid update target' }
    Assert-PlainPath $target
    $parent = Get-Process -Id $job.ParentId -ErrorAction SilentlyContinue
    if ($null -ne $parent -and -not $parent.WaitForExit(120000)) { throw 'Application is still running' }
    $mutex = New-Object Threading.Mutex($false, 'Local\Tobtech.OmnikeyRcoV2')
    try { $locked = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked = $true }
    if (-not $locked) { throw 'Another application is running' }
    if (@(Get-ChildItem -LiteralPath $dataRoot -File | Where-Object { $_.Name -like 'aterstall-*.json' -or $_.Name -like 'usb-pending-*.json' }).Count -gt 0) { throw 'Reader recovery is pending' }
    # Extract into a fresh sibling on the target volume. Prepared loose files are never trusted.
    $candidate = $target + '.new-' + [Guid]::NewGuid().ToString('N')
    [void][IO.Directory]::CreateDirectory($candidate)
    Add-Type -AssemblyName System.IO.Compression
    $memory = [IO.MemoryStream]::new($bytes, $false)
    $zip = [IO.Compression.ZipArchive]::new($memory, [IO.Compression.ZipArchiveMode]::Read, $false)
    if ($zip.Entries.Count -lt 1 -or $zip.Entries.Count -gt 1000) { throw 'Invalid archive entry count' }
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [long]$total = 0
    foreach ($entry in $zip.Entries) {
        $name = $entry.FullName
        if ([string]::IsNullOrEmpty($name) -or $name.StartsWith('/') -or $name.Contains('\') -or $name.Contains(':') -or $name.Contains([string][char]0) -or -not $names.Add($name) -or (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) { throw 'Unsafe archive path' }
        foreach ($part in $name.TrimEnd('/').Split('/')) {
            if ($part -eq '.' -or $part -eq '..' -or $part.Length -eq 0 -or $part.TrimEnd(' ', '.') -cne $part -or $part -match '[<>"|?*\x00-\x1F]' -or $part -match '\A(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?\z') { throw 'Unsafe archive component' }
        }
        $total += $entry.Length
        if ($total -gt 104857600) { throw 'Archive too large' }
        $destination = Join-Path $candidate ($name.Replace('/', '\'))
        if ($name.EndsWith('/')) { [void][IO.Directory]::CreateDirectory($destination); continue }
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
        $inputStream = $entry.Open()
        $outputStream = [IO.File]::Open($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try {
            $buffer = New-Object byte[] 32768
            while (($count = $inputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                if ($outputStream.Length + $count -gt $entry.Length) { throw 'Invalid expanded size' }
                $outputStream.Write($buffer, 0, $count)
            }
            if ($outputStream.Length -ne $entry.Length) { throw 'Truncated archive entry' }
        } finally { $outputStream.Dispose(); $inputStream.Dispose() }
    }
    $zip.Dispose(); $zip = $null
    $memory.Dispose(); $memory = $null
    $entrySource = [IO.File]::ReadAllText((Join-Path $candidate 'Program\Entry.cs'))
    if (-not $entrySource.Contains('AssemblyVersion("' + $asset.Version + '.0")')) { throw 'Unexpected package version' }
    $shell = Join-Path $PSHOME 'powershell.exe'
    $test = Start-Process -FilePath $shell -ArgumentList @('-NoProfile','-STA','-File',('"' + (Join-Path $candidate 'Program\Starta-Bricklasaren.ps1') + '"'),'-SelfTestOnly') -PassThru
    if (-not $test.WaitForExit(120000)) { $test.Kill(); throw 'New version self-test timed out' }
    if ($test.ExitCode -ne 0) { throw 'New version self-test failed' }
    Assert-PlainPath $target
    Assert-PlainPath $candidate
    $backup = $target + '.previous-' + [Guid]::NewGuid().ToString('N')
    [IO.Directory]::Move($target, $backup)
    try { [IO.Directory]::Move($candidate, $target); $candidate = $null }
    catch { [IO.Directory]::Move($backup, $target); $backup = $null; throw }
    $mutex.ReleaseMutex(); $locked = $false
    Start-Process -FilePath (Join-Path $target 'Starta-Bricklasaren.cmd') -WorkingDirectory $target
    [IO.File]::WriteAllText((Join-Path $dataRoot 'uppdatering-status.txt'), 'Uppdatering installerad. Tidigare programmapp finns kvar som .previous-kopia.')
    Remove-Item -LiteralPath $archive -Force
    $oldStage = Join-Path $jobRoot 'package'
    if (Test-Path -LiteralPath $oldStage) { Assert-PlainPath $oldStage; Remove-Item -LiteralPath $oldStage -Recurse -Force }
} catch {
    if ($null -ne $dataRoot) {
        [IO.File]::WriteAllText((Join-Path $dataRoot 'uppdatering-status.txt'), 'Uppdateringen misslyckades. Starta Bricklasaren igen. Tidigare version finns kvar som .previous-kopia om mappbytet hann slutföras.')
    }
    Write-Host 'Uppdateringen kunde inte slutföras. Starta Brickläsaren igen. Tidigare version finns kvar.'
    exit 1
} finally {
    if ($null -ne $zip) { $zip.Dispose() }
    if ($null -ne $memory) { $memory.Dispose() }
    if ($locked) { $mutex.ReleaseMutex() }
    if ($null -ne $mutex) { $mutex.Dispose() }
    if ($null -ne $candidate -and (Test-Path -LiteralPath $candidate)) {
        try { Assert-PlainPath $candidate; Remove-Item -LiteralPath $candidate -Recurse -Force } catch { }
    }
}
