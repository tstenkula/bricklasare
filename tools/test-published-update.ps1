#requires -Version 5.1
[CmdletBinding()]
param([switch]$InstallOnDisposableRunner, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSEdition -ne 'Desktop' -or [Environment]::OSVersion.Platform -ne 'Win32NT') { throw 'Use Windows PowerShell 5.1.' }

# A reproducible acceptance test of the actual, immutable published artifacts.
# No source patching, replacement signing key, profile redirection, or policy change.
$oldHash = '494c69f17980ff687e7758a4602f354967913fe9c3a385c28c83e9f549e87617'
$newHash = 'b36e8878bad3fc7c96d99358d9e01ec58cf163e3c38363fbfbc4a7a2ef0bc496'
$helperHash = '7764dd8e6b4f87aae24e1b628501ceee9a4d01cd473e45e3f0b33e09e6f4aec3'
$feedHash = 'cc104420dc4e9ffdbd2ca2a7d9f5563beb4efda4616c9c2fab899e355b4313d3'
$releaseBase = 'https://github.com/tstenkula/bricklasare/releases/download/'
$feedUrl = 'https://raw.githubusercontent.com/tstenkula/bricklasare/136898a6df968a3e0a99b6333dbe8983e4d464c3/Updates/preview.feed'
$utf8 = New-Object Text.UTF8Encoding($false)
$shell = Join-Path $PSHOME 'powershell.exe'
$tempRoot = [IO.Path]::GetTempPath()
if ($env:RUNNER_TEMP) { $tempRoot = $env:RUNNER_TEMP }
$work = Join-Path $tempRoot ('bricklasare-published-' + [Guid]::NewGuid().ToString('N'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $tempRoot ('bricklasare-published-report-' + [Guid]::NewGuid().ToString('N')) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $OutputDirectory.StartsWith([IO.Path]::GetFullPath($tempRoot).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Reports must stay under the temporary test directory.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'The report directory must not exist.' }
if ($InstallOnDisposableRunner) {
    if ($env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted' -or $env:RUNNER_OS -cne 'Windows' -or $env:GITHUB_REPOSITORY -cne 'tstenkula/bricklasare' -or -not $env:RUNNER_TEMP) {
        throw 'Full installation is permitted only on an opted-in disposable GitHub-hosted runner. Never run it on a personal or self-hosted computer.'
    }
    $allowedEvent = $env:GITHUB_EVENT_NAME -ceq 'workflow_dispatch'
    if ($env:GITHUB_EVENT_NAME -ceq 'pull_request') {
        $event = [IO.File]::ReadAllText($env:GITHUB_EVENT_PATH) | ConvertFrom-Json
        $allowedEvent = $event.pull_request.head.repo.full_name -ceq 'tstenkula/bricklasare' -and $event.pull_request.base.repo.full_name -ceq 'tstenkula/bricklasare' -and -not [string]::IsNullOrWhiteSpace($event.pull_request.head.ref)
    }
    if (-not $allowedEvent) { throw 'Use manual dispatch or a same-repository pull request.' }
}
$vendorRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Tobtech'
if ($InstallOnDisposableRunner -and (Test-Path -LiteralPath $vendorRoot)) { throw 'Existing Tobtech data found; preserve it and refuse the installation test.' }
$ownedData = $false
$gate = $null
$helper = $null
$job = $null
$checks = New-Object 'Collections.Generic.List[string]'
$result = [ordered]@{ From='3.5.2'; To='3.6.0'; Channel='preview'; OldPackageSha256=$oldHash; NewPackageSha256=$newHash; FeedSha256=$feedHash; OriginalHelperSha256=$helperHash; FullInstallRequested=[bool]$InstallOnDisposableRunner; Passed=$false; Checks=@(); Limitations=@('No physical reader test.', 'The normal app relaunch is intentionally stopped; this is not an interactive UI acceptance test.') }

function Confirm([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw ('FAIL: ' + $Name) }
    $script:checks.Add($Name); Write-Host ('PASS: ' + $Name)
}
function Download-Public([string]$Url, [string]$Path, [long]$Maximum) {
    $uri = [Uri]$Url
    for ($redirect = 0; $redirect -lt 6; $redirect++) {
        if ($uri.Scheme -cne 'https' -or -not $uri.IsDefaultPort -or $uri.UserInfo.Length -ne 0 -or $uri.Host -notin @('github.com','raw.githubusercontent.com','release-assets.githubusercontent.com','objects.githubusercontent.com')) { throw 'Untrusted download destination.' }
        $request = [Net.HttpWebRequest]::Create($uri)
        $request.AllowAutoRedirect = $false; $request.Timeout = 30000; $request.ReadWriteTimeout = 30000
        $request.UseDefaultCredentials = $false; $request.UserAgent = 'Tobtech-Published-Update-Acceptance'
        $response = $request.GetResponse()
        try {
            if ([int]$response.StatusCode -ge 300 -and [int]$response.StatusCode -lt 400) { $uri = [Uri]::new($uri, $response.Headers['Location']); continue }
            if ([int]$response.StatusCode -ne 200 -or $response.ContentLength -gt $Maximum) { throw 'Unexpected download response.' }
            $downloadStream = $response.GetResponseStream(); $output = [IO.File]::Open($Path, 'CreateNew', 'Write', 'None')
            try {
                $buffer = New-Object byte[] 32768; $timer = [Diagnostics.Stopwatch]::StartNew()
                while (($count = $downloadStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    if ($output.Length + $count -gt $Maximum -or $timer.Elapsed.TotalSeconds -gt 90) { throw 'Download exceeds its bound.' }
                    $output.Write($buffer, 0, $count)
                }
                if ($output.Length -eq 0) { throw 'Empty download.' }
            } finally { $output.Dispose(); $downloadStream.Dispose() }
            return
        } finally { $response.Dispose() }
    }
    throw 'Too many download redirects.'
}
function Quote-Path([string]$Path) {
    if ($Path.Contains('"') -or $Path.Contains("`r") -or $Path.Contains("`n")) { throw 'Unsupported process argument.' }
    return '"' + $Path + '"'
}
function New-TestProcess([string]$Arguments) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $script:shell; $info.Arguments = $Arguments; $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $info.EnvironmentVariables['BRICKLASARE_TEST_ARCHIVE'] = $script:newArchive
    $info.EnvironmentVariables.Remove('BRICKLASARE_TEST_IMAGES')
    return [Diagnostics.Process]::Start($info)
}
function Verify-OriginalHelper([string]$Name, [bool]$Expected) {
    $process = New-TestProcess ('-NoProfile -NonInteractive -File ' + (Quote-Path $script:oldHelper) + ' -Plan ' + (Quote-Path $script:verifyPlan) + ' -VerifyPackageOnly')
    try {
        $out = $process.StandardOutput.ReadToEndAsync(); $err = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Signature verification timed out.' }
        [IO.File]::WriteAllText((Join-Path $script:OutputDirectory ($Name + '.log')), $out.Result + $err.Result, $script:utf8)
        $expectedExit = if ($Expected) { 0 } else { 1 }
        Confirm ($process.ExitCode -eq $expectedExit) $Name
    } finally { $process.Dispose() }
}
function File-Manifest([string]$Directory) {
    $manifest = @{}
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Recurse -File) {
        $relative = $file.FullName.Substring($Directory.TrimEnd('\').Length + 1)
        $manifest[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    return $manifest
}
function Same-Manifest($Before, $After) {
    if ($Before.Count -ne $After.Count) { return $false }
    foreach ($name in $Before.Keys) { if (-not $After.ContainsKey($name) -or $Before[$name] -cne $After[$name]) { return $false } }
    return $true
}

[void][IO.Directory]::CreateDirectory($work)
[void][IO.Directory]::CreateDirectory($OutputDirectory)
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $oldArchive = Join-Path $work 'old.zip'; $newArchive = Join-Path $work 'new.zip'; $feed = Join-Path $work 'original.feed'
    Download-Public ($releaseBase + 'v3.5.2-preview.1/Bricklasaren-3.5.2-Windows.zip') $oldArchive 52428800
    Download-Public ($releaseBase + 'v3.6.0-preview.1/Bricklasaren-3.6.0-Windows.zip') $newArchive 52428800
    Download-Public $feedUrl $feed 65536
    Confirm ((Get-FileHash $oldArchive).Hash -ieq $oldHash) 'Anonymous download matches immutable 3.5.2 ZIP'
    Confirm ((Get-FileHash $newArchive).Hash -ieq $newHash) 'Anonymous download matches immutable 3.6.0 ZIP'
    Confirm ((Get-FileHash $feed).Hash -ieq $feedHash) 'Original signed 3.6.0 feed is unchanged'
    $target = Join-Path $work 'installed app'; $expected = Join-Path $work 'expected-new'
    [IO.Compression.ZipFile]::ExtractToDirectory($oldArchive, $target)
    [IO.Compression.ZipFile]::ExtractToDirectory($newArchive, $expected)
    $oldHelper = Join-Path $target 'Program\Install-Update.ps1'
    Confirm ((Get-FileHash $oldHelper).Hash -ieq $helperHash) 'Original 3.5.2 installer is unmodified'
    $verify = Join-Path $work 'verify'; [void][IO.Directory]::CreateDirectory($verify)
    $verifyPlan = Join-Path $verify 'plan.json'
    [IO.File]::WriteAllText($verifyPlan, '{"Channel":"preview"}', $utf8)
    Copy-Item -LiteralPath $feed -Destination (Join-Path $verify 'release.feed')
    Copy-Item -LiteralPath $newArchive -Destination (Join-Path $verify 'package.zip')
    Verify-OriginalHelper 'Original signature and package accepted by 3.5.2' $true
    $changed = [IO.File]::ReadAllBytes($newArchive); $changed[$changed.Length - 1] = $changed[$changed.Length - 1] -bxor 1
    [IO.File]::WriteAllBytes((Join-Path $verify 'package.zip'), $changed)
    Verify-OriginalHelper 'Tampered published package is rejected' $false
    Copy-Item -LiteralPath $newArchive -Destination (Join-Path $verify 'package.zip') -Force
    $changed = [IO.File]::ReadAllBytes($feed); $changed[$changed.Length - 3] = $changed[$changed.Length - 3] -bxor 1
    [IO.File]::WriteAllBytes((Join-Path $verify 'release.feed'), $changed)
    Verify-OriginalHelper 'Tampered original feed is rejected' $false

    if ($InstallOnDisposableRunner) {
        # A fresh hosted VM is required. Refuse even an unrelated connected HID OMNIKEY.
        $devices = @(Get-CimInstance -ClassName Win32_PnPEntity | Where-Object { $_.PNPDeviceID -match '(?i)VID_076B' })
        Confirm ($devices.Count -eq 0) 'Disposable runner has no physical OMNIKEY reader'
        if (Test-Path -LiteralPath $vendorRoot) { throw 'Tobtech data appeared during download; refusing installation.' }
        $data = Join-Path $vendorRoot 'Omnikey-RCO-v2'; [void][IO.Directory]::CreateDirectory($data); $ownedData = $true
        $sentinels = @{
            'formatval.json' = '{"Mode":"Kombi","RcoChip":"M"}'
            'utseende.json' = '{"Schema":1,"Organization":"","Subtitle":"","AccentHex":"#003B55","LogoFile":""}'
            'arbetsflode.json' = '{"Copy":0,"Sounds":false,"Channel":"preview","AutoCheck":false}'
            'acceptance-sentinel.txt' = 'Synthetic acceptance settings; no reader or card data.'
        }
        foreach ($name in $sentinels.Keys) { [IO.File]::WriteAllText((Join-Path $data $name), $sentinels[$name], $utf8) }
        $settingsBefore = File-Manifest $data
        [IO.File]::WriteAllText((Join-Path $target 'old-install-sentinel.txt'), 'Retain the complete old installation.', $utf8)
        $oldFiles = File-Manifest $target; $newFiles = File-Manifest $expected
        $stage = Join-Path (Join-Path $data 'updates') ([Guid]::NewGuid().ToString('N')); [void][IO.Directory]::CreateDirectory($stage)
        Copy-Item -LiteralPath $newArchive -Destination (Join-Path $stage 'package.zip')
        Copy-Item -LiteralPath $feed -Destination (Join-Path $stage 'release.feed')
        Copy-Item -LiteralPath $oldHelper -Destination (Join-Path $stage 'Install-Update.ps1')
        Confirm ((Get-FileHash (Join-Path $stage 'Install-Update.ps1')).Hash -ieq $helperHash) 'Staged helper retains the original trusted bytes'

        # The helper waits for this owned process before spawning anything. Assign
        # it to our job first, then release that gate. Closing the job kills only
        # this installer and its descendants, including its normal app relaunch.
        Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace Tobtech.Acceptance {
    public sealed class OwnedJob : IDisposable {
        [StructLayout(LayoutKind.Sequential)] struct Basic { public long ProcessTime, JobTime; public uint Flags; public UIntPtr MinWorking, MaxWorking; public uint ActiveLimit; public UIntPtr Affinity; public uint Priority, Scheduling; }
        [StructLayout(LayoutKind.Sequential)] struct Counters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] struct Extended { public Basic Basic; public Counters IO; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
        [DllImport("kernel32.dll", SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        IntPtr handle;
        public OwnedJob() {
            handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero) throw new Win32Exception();
            Extended limit = new Extended(); limit.Basic.Flags = 0x2000; // KILL_ON_JOB_CLOSE; no breakaway.
            int size = Marshal.SizeOf(typeof(Extended)); IntPtr memory = Marshal.AllocHGlobal(size);
            try { Marshal.StructureToPtr(limit, memory, false); if (!SetInformationJobObject(handle, 9, memory, (uint)size)) throw new Win32Exception(); }
            catch { Dispose(); throw; } finally { Marshal.FreeHGlobal(memory); }
        }
        public void Add(Process process) { if (!AssignProcessToJobObject(handle, process.Handle)) throw new Win32Exception(); }
        public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
    }
}
'@
        $job = New-Object Tobtech.Acceptance.OwnedJob
        $gate = New-TestProcess '-NoProfile -NonInteractive -Command "Start-Sleep -Seconds 180"'
        $plan = Join-Path $stage 'plan.json'
        [IO.File]::WriteAllText($plan, (@{ Target=$target; Stage=(Join-Path $stage 'package'); ParentId=$gate.Id; Data=$data; Channel='preview' } | ConvertTo-Json), $utf8)
        $helper = New-TestProcess ('-NoProfile -NonInteractive -File ' + (Quote-Path (Join-Path $stage 'Install-Update.ps1')) + ' -Plan ' + (Quote-Path $plan))
        $helperOut = $helper.StandardOutput.ReadToEndAsync(); $helperErr = $helper.StandardError.ReadToEndAsync()
        $job.Add($helper)
        $gate.Kill(); $gate.WaitForExit(); $gate.Dispose(); $gate = $null
        if (-not $helper.WaitForExit(240000)) { throw 'Full installer exceeded the four-minute acceptance limit.' }
        $exit = $helper.ExitCode
        $job.Dispose(); $job = $null
        if (-not $helperOut.Wait(10000) -or -not $helperErr.Wait(10000)) { throw 'Installer child output did not close after terminating the owned job.' }
        [IO.File]::WriteAllText((Join-Path $OutputDirectory 'full-installer.log'), $helperOut.Result + $helperErr.Result, $utf8)
        Confirm ($exit -eq 0) 'Unmodified 3.5.2 helper completes the real 3.6.0 installation'
        $status = Join-Path $data 'uppdatering-status.txt'; $selfTest = Join-Path $data 'sjalvtest-reception.txt'
        Copy-Item -LiteralPath $status -Destination (Join-Path $OutputDirectory 'installer-status.txt')
        Copy-Item -LiteralPath $selfTest -Destination (Join-Path $OutputDirectory 'installed-self-test.txt')
        Confirm ([IO.File]::ReadAllText($status).StartsWith('Uppdatering installerad.')) 'Installer reports a completed installation'
        Confirm ([IO.File]::ReadAllText($selfTest).Contains('3.6.0') -and [IO.File]::ReadAllText($selfTest).Contains('tester av inställningar och resurshantering godkända.')) 'Published new application passes its actual offline self-tests'
        Confirm (Same-Manifest $newFiles (File-Manifest $target)) 'Installed files exactly match the published 3.6.0 ZIP'
        $backups = @(Get-ChildItem -LiteralPath $work -Directory | Where-Object { $_.Name -like 'installed app.previous-*' })
        Confirm ($backups.Count -eq 1 -and (Same-Manifest $oldFiles (File-Manifest $backups[0].FullName))) 'Previous installation is preserved byte for byte as a backup'
        foreach ($name in $settingsBefore.Keys) { Confirm ((Get-FileHash -LiteralPath (Join-Path $data $name)).Hash -ceq $settingsBefore[$name]) ('Personal setting preserved: ' + $name) }
        Confirm (-not (Test-Path -LiteralPath (Join-Path $stage 'package.zip'))) 'Verified staging archive is removed after successful installation'
    } else { $result.Limitations += 'Full installation, settings preservation, and backup were not tested; -InstallOnDisposableRunner was omitted.' }
    $result.Passed = $true
} catch {
    $result['Failure'] = $_.Exception.Message
    throw
} finally {
    if ($null -ne $job) { $job.Dispose() }
    if ($null -ne $gate) { if (-not $gate.HasExited) { $gate.Kill(); $gate.WaitForExit() }; $gate.Dispose() }
    if ($null -ne $helper) { if (-not $helper.HasExited) { $helper.Kill(); $helper.WaitForExit() }; $helper.Dispose() }
    $result.Checks = @($checks.ToArray())
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'summary.json'), ($result | ConvertTo-Json -Depth 6), $utf8)
    if ($ownedData) { Remove-Item -LiteralPath $vendorRoot -Recurse -Force }
    Remove-Item -LiteralPath $work -Recurse -Force
}
# The final negative verifier is intentionally nonzero; do not inherit its status.
exit 0
