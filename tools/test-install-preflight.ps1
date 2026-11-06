#requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Use Windows PowerShell 5.1.' }
$root = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class InstallWindowProbe {
    delegate void Callback(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time);
    static Callback callback = Observe;
    static IntPtr hook;
    public static uint Target;
    public static int Shown;
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,Callback cb,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr value);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    static void Observe(IntPtr h,uint e,IntPtr window,int obj,int child,uint thread,uint time) {
        uint pid; GetWindowThreadProcessId(window,out pid);
        if (pid==Target && obj==0 && child==0 && GetAncestor(window,2)==window && IsWindowVisible(window)) Shown++;
    }
    public static void Begin() { hook=SetWinEventHook(0x8002,0x8002,IntPtr.Zero,callback,0,0,2); if(hook==IntPtr.Zero) throw new Exception("Window event probe could not start"); }
    public static void End() { if(hook!=IntPtr.Zero) UnhookWinEvent(hook); hook=IntPtr.Zero; }
}
'@
function Data-Manifest {
    $directory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Tobtech\Omnikey-RCO-v2'
    if (-not (Test-Path -LiteralPath $directory)) { return 'absent' }
    $rows = @(Get-ChildItem -LiteralPath $directory -Recurse -File | Sort-Object FullName | ForEach-Object { $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName).Hash })
    return 'exists|' + ($rows -join '|')
}
function Probe-Process([string]$Arguments, [int]$Exit, [bool]$ExpectWindows) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = Join-Path $PSHOME 'powershell.exe'
    $info.Arguments = $Arguments
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    [InstallWindowProbe]::Target = 0; [InstallWindowProbe]::Shown = 0
    $process = [Diagnostics.Process]::Start($info)
    [InstallWindowProbe]::Target = [uint32]$process.Id
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    $clock = [Diagnostics.Stopwatch]::StartNew()
    try {
        while (-not $process.HasExited) {
            [Windows.Forms.Application]::DoEvents()
            if ($clock.Elapsed.TotalSeconds -gt 60) { $process.Kill(); throw 'Preflight probe timed out.' }
            Start-Sleep -Milliseconds 20
        }
        $process.WaitForExit(); [Windows.Forms.Application]::DoEvents()
        if ($process.ExitCode -ne $Exit) { throw ('Unexpected preflight exit: ' + $stderr.Result) }
        if (([InstallWindowProbe]::Shown -gt 0) -ne $ExpectWindows) { throw 'Unexpected visible application-window events.' }
    } finally { $process.Dispose() }
}
$temp = Join-Path ([IO.Path]::GetTempPath()) ('bricklasare-preflight-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temp)
[InstallWindowProbe]::Begin()
try {
    $positive = Join-Path $temp 'window-probe.ps1'
    [IO.File]::WriteAllText($positive, 'Add-Type -AssemblyName System.Windows.Forms; $f=New-Object Windows.Forms.Form; $f.Show(); [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 500; $f.Dispose()')
    Probe-Process ('-NoProfile -STA -File "' + $positive + '"') 0 $true
    Write-Host 'PASS: visible-window probe detects an actual child form'
    $before = Data-Manifest
    foreach ($mode in @('-ValidateInstallOnly','-SelfTestOnly')) {
        Probe-Process ('-NoProfile -NonInteractive -STA -File "' + (Join-Path $root 'Program\Starta-Bricklasaren.ps1') + '" ' + $mode) 0 $false
        if ((Data-Manifest) -cne $before) { throw 'Quiet validation changed user files.' }
        Write-Host ('PASS: ' + $mode + ' opens zero application windows and changes no user files')
    }
    # An incomplete package fails quietly as well, before a dialog/report can open.
    $bad = Join-Path $temp 'incomplete'; [void][IO.Directory]::CreateDirectory((Join-Path $bad 'Program'))
    Copy-Item -LiteralPath (Join-Path $root 'Program\Starta-Bricklasaren.ps1') -Destination (Join-Path $bad 'Program\Starta-Bricklasaren.ps1')
    Probe-Process ('-NoProfile -NonInteractive -STA -File "' + (Join-Path $bad 'Program\Starta-Bricklasaren.ps1') + '" -SelfTestOnly') 1 $false
    if ((Data-Manifest) -cne $before) { throw 'Failed quiet validation changed user files.' }
    Write-Host 'PASS: incomplete package fails with zero windows and no user-data writes'
} finally {
    [InstallWindowProbe]::End()
    [IO.Directory]::Delete($temp,$true)
}
exit 0
