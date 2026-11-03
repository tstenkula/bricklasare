#requires -Version 5.1
param([Parameter(Mandatory=$true)][string]$Plan)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$job = $null
$backup = $null
$swapped = $false
$mutex = $null
$locked = $false
try {
    $planPath = [IO.Path]::GetFullPath($Plan)
    $job = Get-Content -LiteralPath $planPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $jobRoot = Split-Path -Parent $planPath
    $target = [IO.Path]::GetFullPath($job.Target).TrimEnd('\')
    $stage = [IO.Path]::GetFullPath($job.Stage)
    if ($stage -ne (Join-Path $jobRoot 'package') -or $target -eq [IO.Path]::GetPathRoot($target) -or -not (Test-Path -LiteralPath (Join-Path $target 'Program\Entry.cs'))) { throw 'Invalid update plan' }
    $archive = Join-Path $jobRoot 'package.zip'
    if ((Get-Item -LiteralPath $archive).Length -ne $job.Size -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $job.Sha256) { throw 'Package verification failed' }
    $parent = Get-Process -Id $job.ParentId -ErrorAction SilentlyContinue
    if ($null -ne $parent -and -not $parent.WaitForExit(120000)) { throw 'Application is still running' }
    $mutex = New-Object Threading.Mutex($false, 'Local\Tobtech.OmnikeyRcoV2')
    try { $locked = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked = $true }
    if (-not $locked) { throw 'Another application is running' }
    if (@(Get-ChildItem -LiteralPath $job.Data -File | Where-Object { $_.Name -like 'aterstall-*.json' -or $_.Name -like 'usb-pending-*.json' }).Count -gt 0) { throw 'Reader recovery is pending' }
    # Re-extract the already verified archive, never trust files changed after preparation.
    Remove-Item -LiteralPath $stage -Recurse -Force
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $stage)
    $entry = [IO.File]::ReadAllText((Join-Path $stage 'Program\Entry.cs'))
    if (-not $entry.Contains('AssemblyVersion("' + $job.Version + '.0")')) { throw 'Unexpected package version' }
    $shell = Join-Path $PSHOME 'powershell.exe'
    $test = Start-Process -FilePath $shell -ArgumentList @('-NoProfile','-STA','-File',('"' + (Join-Path $stage 'Program\Starta-Bricklasaren.ps1') + '"'),'-SelfTestOnly') -PassThru
    if (-not $test.WaitForExit(120000)) { $test.Kill(); throw 'New version self-test timed out' }
    if ($test.ExitCode -ne 0) { throw 'New version self-test failed' }
    # Stage on the same volume. Settings live outside the replaced program directory.
    $candidate = $target + '.new-' + [Guid]::NewGuid().ToString('N')
    Copy-Item -LiteralPath $stage -Destination $candidate -Recurse
    $backup = $target + '.previous-' + [Guid]::NewGuid().ToString('N')
    [IO.Directory]::Move($target, $backup)
    try { [IO.Directory]::Move($candidate, $target); $swapped = $true }
    catch { [IO.Directory]::Move($backup, $target); $backup = $null; throw }
    $mutex.ReleaseMutex(); $locked = $false
    Start-Process -FilePath (Join-Path $target 'Starta-Bricklasaren.cmd') -WorkingDirectory $target
    [IO.File]::WriteAllText((Join-Path $job.Data 'uppdatering-status.txt'), 'Uppdatering installerad. Tidigare programmapp finns kvar som .previous-kopia.')
    Remove-Item -LiteralPath $stage -Recurse -Force
    Remove-Item -LiteralPath $archive -Force
} catch {
    # No raw error paths, card data, credentials or usernames enter the diagnostic file.
    if ($null -ne $job) {
        [IO.File]::WriteAllText((Join-Path $job.Data 'uppdatering-status.txt'), 'Uppdateringen misslyckades. Starta Bricklasaren igen. Om mappbytet genomfördes finns tidigare version i .previous-mappen.')
    }
    Write-Host 'Uppdateringen kunde inte slutföras. Starta Brickläsaren igen. Tidigare version finns kvar.'
    exit 1
} finally {
    if ($locked) { $mutex.ReleaseMutex() }
    if ($null -ne $mutex) { $mutex.Dispose() }
}
