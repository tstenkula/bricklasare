#requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$InstallOnDisposableRunner,
    [string]$OutputDirectory,
    [ValidateSet('historical', 'stable-3.6.1', 'stable-3.6.2', 'stable-3.6.4', 'preview-3.6.4', 'stable-3.6.5', 'preview-3.6.5')][string]$Case = 'historical'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Case = $Case.ToLowerInvariant()
if ($PSVersionTable.PSEdition -ne 'Desktop' -or [Environment]::OSVersion.Platform -ne 'Win32NT') { throw 'Use Windows PowerShell 5.1.' }

# A reproducible acceptance test of the actual, immutable published artifacts.
# No source patching, replacement signing key, profile redirection, or policy change.
$sourceVersion = '3.5.2'; $targetVersion = '3.6.0'; $channel = 'preview'
$sourceTag = 'v3.5.2-preview.1'; $targetTag = 'v3.6.0-preview.1'
$oldSize = 205960; $newSize = 213221
$oldHash = '494c69f17980ff687e7758a4602f354967913fe9c3a385c28c83e9f549e87617'
$newHash = 'b36e8878bad3fc7c96d99358d9e01ec58cf163e3c38363fbfbc4a7a2ef0bc496'
$helperHash = '7764dd8e6b4f87aae24e1b628501ceee9a4d01cd473e45e3f0b33e09e6f4aec3'
$feedHash = 'cc104420dc4e9ffdbd2ca2a7d9f5563beb4efda4616c9c2fab899e355b4313d3'
$releaseBase = 'https://github.com/tstenkula/bricklasare/releases/download/'
$feedUrl = 'https://raw.githubusercontent.com/tstenkula/bricklasare/136898a6df968a3e0a99b6333dbe8983e4d464c3/Updates/preview.feed'
# The second case is intentionally unavailable until the real signed stable
# fixture is committed. Its target bytes are verified by the pinned prior helper.
$fixture = $null
if ($Case -ceq 'stable-3.6.1') {
    $sourceVersion = '3.6.0'; $targetVersion = '3.6.1'; $channel = 'stable'
    $sourceTag = 'v3.6.0-preview.1'; $targetTag = 'v3.6.1'
    $oldSize = 213221; $newSize = $null
    $oldHash = 'b36e8878bad3fc7c96d99358d9e01ec58cf163e3c38363fbfbc4a7a2ef0bc496'
    $helperHash = '1105c6f5590305e797f37849699baee8671ffdda12a604145624d88d8904dad5'
    $newHash = $null; $feedHash = $null
    $fixture = Join-Path $PSScriptRoot 'fixtures\published-stable-3.6.1.feed'
    if (-not (Test-Path -LiteralPath $fixture -PathType Leaf)) { throw 'The reviewed signed 3.6.1 stable fixture is required.' }
    $fixtureFile = Get-Item -LiteralPath $fixture -Force
    if ($fixtureFile.Length -lt 1 -or $fixtureFile.Length -gt 65536 -or ($fixtureFile.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Invalid stable fixture file.' }
}
if ($Case -ceq 'stable-3.6.2') {
    $sourceVersion = '3.6.1'; $targetVersion = '3.6.2'; $channel = 'stable'
    $sourceTag = 'v3.6.1'; $targetTag = 'v3.6.2'
    $oldSize = 218479; $newSize = $null
    $oldHash = '74f1ffedbe977a90eda73652135edc06f9912e577286dcb435b846454589e79f'
    $helperHash = '90bfb42a511ed30384bc811498fe7cc09e2a91f86d53c29b8427e5bdcef67d51'
    $newHash = $null; $feedHash = $null
    $fixture = Join-Path $PSScriptRoot 'fixtures\published-stable-3.6.2.feed'
    if (-not (Test-Path -LiteralPath $fixture -PathType Leaf)) { throw 'The reviewed signed 3.6.2 stable fixture is required.' }
    $fixtureFile = Get-Item -LiteralPath $fixture -Force
    if ($fixtureFile.Length -lt 1 -or $fixtureFile.Length -gt 65536 -or ($fixtureFile.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Invalid stable fixture file.' }
}
if ($Case -ceq 'stable-3.6.4' -or $Case -ceq 'preview-3.6.4') {
    $sourceVersion = '3.6.2'; $targetVersion = '3.6.4'
    $channel = if ($Case -ceq 'preview-3.6.4') { 'preview' } else { 'stable' }
    $sourceTag = 'v3.6.2'
    $targetTag = if ($channel -ceq 'preview') { 'v3.6.4-preview.1' } else { 'v3.6.4' }
    $oldSize = 233016; $newSize = $null
    $oldHash = 'ba51f798b6abd865c74804b4e7a2af490f09149d9b5bbf9f6b3f53290c274753'
    $helperHash = 'e276d096182fa63fd4f818c45e46bdede099e14a7befffb667647e046078e9db'
    $newHash = $null; $feedHash = $null
    $fixture = Join-Path $PSScriptRoot ('fixtures\published-' + $Case + '.feed')
    if (-not (Test-Path -LiteralPath $fixture -PathType Leaf)) { throw 'The reviewed signed fixture is required.' }
    $fixtureFile = Get-Item -LiteralPath $fixture -Force
    if ($fixtureFile.Length -lt 1 -or $fixtureFile.Length -gt 65536 -or ($fixtureFile.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Invalid signed fixture file.' }
}
if ($Case -ceq 'stable-3.6.5' -or $Case -ceq 'preview-3.6.5') {
    $sourceVersion = '3.6.4'; $targetVersion = '3.6.5'
    $channel = if ($Case -ceq 'preview-3.6.5') { 'preview' } else { 'stable' }
    $sourceTag = 'v3.6.4'
    $targetTag = if ($channel -ceq 'preview') { 'v3.6.5-preview.1' } else { 'v3.6.5' }
    $oldSize = 242157; $newSize = $null
    $oldHash = 'a95dbd8356d02e70ac70a55775d75e3e7ca22967a407192d067ff3991be16765'
    $helperHash = '3baac07899163c1da406f579b4e8fa766cdfb510918e2fc3cf2863da1f9d451e'
    $newHash = $null; $feedHash = $null
    $fixture = Join-Path $PSScriptRoot ('fixtures\published-' + $Case + '.feed')
    if (-not (Test-Path -LiteralPath $fixture -PathType Leaf)) { throw 'The reviewed signed fixture is required.' }
    $fixtureFile = Get-Item -LiteralPath $fixture -Force
    if ($fixtureFile.Length -lt 1 -or $fixtureFile.Length -gt 65536 -or ($fixtureFile.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Invalid signed fixture file.' }
}
$oldUrl = $releaseBase + $sourceTag + '/Bricklasaren-' + $sourceVersion + '-Windows.zip'
$newUrl = $releaseBase + $targetTag + '/Bricklasaren-' + $targetVersion + '-Windows.zip'
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
$result = [ordered]@{ Case=$Case; From=$sourceVersion; To=$targetVersion; Channel=$channel; OldPackageSha256=$oldHash; NewPackageSha256=$newHash; FeedSha256=$feedHash; OriginalHelperSha256=$helperHash; FullInstallRequested=[bool]$InstallOnDisposableRunner; Passed=$false; Checks=@(); Limitations=@('No physical reader test.', 'The normal app relaunch is intentionally stopped; this is not an interactive UI acceptance test.') }

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
    if ($null -eq $fixture) {
        Download-Public $feedUrl $feed 65536
        Confirm ((Get-FileHash $feed).Hash -ieq $feedHash) 'Original signed 3.6.0 feed is unchanged'
    } else {
        Copy-Item -LiteralPath $fixture -Destination $feed
        $feedHash = (Get-FileHash -LiteralPath $feed).Hash.ToLowerInvariant()
        $result.FeedSha256 = $feedHash
    }
    # Metadata is constrained before download. Authenticity is established below
    # by the original helper, before extracting or executing any target code.
    $lines = [IO.File]::ReadAllLines($feed)
    Confirm ($lines.Length -eq 3 -and $lines[0] -ceq 'BRICKLASARE-UPDATE-1') 'Expected signed feed envelope'
    $payload = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($lines[1])) | ConvertFrom-Json
    $asset = $payload.Windows
    Confirm ($payload.Schema -eq 1 -and $payload.Channel -ceq $channel -and $asset.Version -ceq $targetVersion -and $asset.Url -ceq $newUrl) 'Feed describes exactly the selected target release'
    Confirm (($asset.Size -is [int] -or $asset.Size -is [long]) -and $asset.Size -gt 0 -and $asset.Size -le 52428800 -and $asset.Sha256 -cmatch '\A[a-f0-9]{64}\z') 'Target size and SHA-256 are bounded'
    if ($Case -ceq 'historical') {
        Confirm ($asset.Size -eq $newSize -and $asset.Sha256 -ceq $newHash) 'Historical feed retains reviewed package pins'
    } else {
        $newSize = [long]$asset.Size; $newHash = [string]$asset.Sha256
        $result.NewPackageSha256 = $newHash
    }
    Download-Public $oldUrl $oldArchive $oldSize
    Download-Public $newUrl $newArchive $newSize
    Confirm ((Get-Item -LiteralPath $oldArchive).Length -eq $oldSize -and (Get-FileHash $oldArchive).Hash -ieq $oldHash) ('Anonymous download matches immutable ' + $sourceVersion + ' ZIP')
    Confirm ((Get-Item -LiteralPath $newArchive).Length -eq $newSize -and (Get-FileHash $newArchive).Hash -ieq $newHash) ('Anonymous download matches ' + $targetVersion + ' feed metadata')
    $target = Join-Path $work 'installed app'; $expected = Join-Path $work 'expected-new'
    [IO.Compression.ZipFile]::ExtractToDirectory($oldArchive, $target)
    $oldHelper = Join-Path $target 'Program\Install-Update.ps1'
    Confirm ((Get-FileHash $oldHelper).Hash -ieq $helperHash) ('Original ' + $sourceVersion + ' installer is unmodified')
    $verify = Join-Path $work 'verify'; [void][IO.Directory]::CreateDirectory($verify)
    $verifyPlan = Join-Path $verify 'plan.json'
    [IO.File]::WriteAllText($verifyPlan, (@{ Channel=$channel } | ConvertTo-Json -Compress), $utf8)
    Copy-Item -LiteralPath $feed -Destination (Join-Path $verify 'release.feed')
    Copy-Item -LiteralPath $newArchive -Destination (Join-Path $verify 'package.zip')
    Verify-OriginalHelper ('Original signature and package accepted by ' + $sourceVersion) $true
    [IO.Compression.ZipFile]::ExtractToDirectory($newArchive, $expected)
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
            'arbetsflode.json' = ('{"Copy":0,"Sounds":false,"Channel":"' + $channel + '","AutoCheck":false}')
            'lasarljud-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.json' = '{"Schema":1,"ReaderId":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","Firmware":"00000000","Original":"000000000000000000000000000000000000000000000000000000000000000000000000000000"}'
            'acceptance-sentinel.txt' = 'Synthetic acceptance settings; no reader or card data.'
        }
        if ($targetVersion -ceq '3.6.5') {
            $sentinels['lasarljud-BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.json'] = ('{"Schema":2,"ReaderId":"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB","Firmware":"04020100","Protocol":"Gen1WebSequences-v1","TableLength":143,"Original":"' + ('00' * 52) + '"}')
        }
        foreach ($name in $sentinels.Keys) { [IO.File]::WriteAllText((Join-Path $data $name), $sentinels[$name], $utf8) }
        $settingsBefore = File-Manifest $data
        [IO.File]::WriteAllText((Join-Path $target 'old-install-sentinel.txt'), 'Retain the complete old installation.', $utf8)
        if ($targetVersion -ceq '3.6.4' -or $targetVersion -ceq '3.6.5') {
            $oldLauncher = Join-Path $target 'Starta-Bricklasaren.cmd'
            Set-Content -LiteralPath $oldLauncher -Stream Zone.Identifier -Value @('[ZoneTransfer]', 'ZoneId=3') -Encoding ASCII
            Confirm ((Get-Content -LiteralPath $oldLauncher -Stream Zone.Identifier) -contains 'ZoneId=3') 'Old launcher has a synthetic browser download marker'
        }
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
        [IO.File]::WriteAllText($plan, (@{ Target=$target; Stage=(Join-Path $stage 'package'); ParentId=$gate.Id; Data=$data; Channel=$channel } | ConvertTo-Json), $utf8)
        $helper = New-TestProcess ('-NoProfile -NonInteractive -File ' + (Quote-Path (Join-Path $stage 'Install-Update.ps1')) + ' -Plan ' + (Quote-Path $plan))
        $helperOut = $helper.StandardOutput.ReadToEndAsync(); $helperErr = $helper.StandardError.ReadToEndAsync()
        $job.Add($helper)
        $gate.Kill(); $gate.WaitForExit(); $gate.Dispose(); $gate = $null
        if (-not $helper.WaitForExit(240000)) { throw 'Full installer exceeded the four-minute acceptance limit.' }
        $exit = $helper.ExitCode
        $job.Dispose(); $job = $null
        if (-not $helperOut.Wait(10000) -or -not $helperErr.Wait(10000)) { throw 'Installer child output did not close after terminating the owned job.' }
        [IO.File]::WriteAllText((Join-Path $OutputDirectory 'full-installer.log'), $helperOut.Result + $helperErr.Result, $utf8)
        Confirm ($exit -eq 0) ('Unmodified ' + $sourceVersion + ' helper completes the real ' + $targetVersion + ' installation')
        $status = Join-Path $data 'uppdatering-status.txt'; $selfTest = Join-Path $data 'sjalvtest-reception.txt'
        Copy-Item -LiteralPath $status -Destination (Join-Path $OutputDirectory 'installer-status.txt')
        Copy-Item -LiteralPath $selfTest -Destination (Join-Path $OutputDirectory 'installed-self-test.txt')
        Confirm ([IO.File]::ReadAllText($status).StartsWith('Uppdatering installerad.')) 'Installer reports a completed installation'
        Confirm ([IO.File]::ReadAllText($selfTest).Contains($targetVersion) -and [IO.File]::ReadAllText($selfTest).Contains('tester av inställningar och resurshantering godkända.')) 'Published new application passes its actual offline self-tests'
        Confirm (Same-Manifest $newFiles (File-Manifest $target)) ('Installed files exactly match the published ' + $targetVersion + ' ZIP')
        $backups = @(Get-ChildItem -LiteralPath $work -Directory | Where-Object { $_.Name -like 'installed app.previous-*' })
        Confirm ($backups.Count -eq 1 -and (Same-Manifest $oldFiles (File-Manifest $backups[0].FullName))) 'Previous installation is preserved byte for byte as a backup'
        if ($targetVersion -ceq '3.6.4' -or $targetVersion -ceq '3.6.5') {
            $newLauncher = Join-Path $target 'Starta-Bricklasaren.cmd'
            Confirm (@(Get-Item -LiteralPath $newLauncher -Stream Zone.Identifier -ErrorAction SilentlyContinue).Count -eq 0) 'Verified replacement creates a fresh launcher without inheriting the old download marker'
            $previousLauncher = Join-Path $backups[0].FullName 'Starta-Bricklasaren.cmd'
            Confirm ((Get-Content -LiteralPath $previousLauncher -Stream Zone.Identifier) -contains 'ZoneId=3') 'Previous launcher retains its original download marker in the backup'
        }
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
