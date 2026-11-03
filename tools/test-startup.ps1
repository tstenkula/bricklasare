#requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Run with Windows PowerShell 5.1.' }
if ('Tobtech.OmnikeyRco.Entry' -as [type]) { throw 'Run this test in a fresh PowerShell process.' }

# Compile the real entry point against instrumented boundaries. No UI, reader,
# network, or filesystem-writing implementation is included in this assembly.
# The production launcher and command-line switches are unchanged.
$root = Split-Path -Parent $PSScriptRoot
$entry = [IO.File]::ReadAllText((Join-Path $root 'Program\Entry.cs'), [Text.Encoding]::UTF8)
$boundaries = @'
namespace Tobtech.OmnikeyRco
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    public static class StartupProbe
    {
        public static readonly List<string> Calls = new List<string>();
        public static string FailAt, Report;
        public static void Reset(string failAt) { Calls.Clear(); FailAt = failAt; Report = null; }
        public static void Record(string name)
        {
            Calls.Add(name);
            if (name == FailAt) throw new InvalidOperationException("Expected startup test failure.");
        }
    }
    public static class ReceptionFiles
    {
        public static string PackagePath;
        public static string DataPath
        {
            get { StartupProbe.Record("data-path"); return Path.GetTempPath(); }
        }
        public static void WriteAtomic(string path, string text)
        {
            StartupProbe.Record(Path.GetFileName(path) == "sjalvtest-reception.txt" ? "report" : "error-report");
            StartupProbe.Report = text;
        }
        public static string SafeError(Exception error) { return error.GetType().Name; }
    }
    public static class SelfTests
    {
        public static int Run() { StartupProbe.Record("core"); return 11; }
    }
    public static class ReceptionTests
    {
        public static int Run() { StartupProbe.Record("reception"); return 22; }
    }
    public static class ReliabilityTests
    {
        public static int Run() { StartupProbe.Record("reliability"); return 33; }
    }
    public static class ReceptionProgram
    {
        public static void Run(string path)
        {
            if (ReceptionFiles.PackagePath != Path.GetFullPath(path))
                throw new InvalidOperationException("Package path was not initialized.");
            StartupProbe.Record("application");
        }
    }
}
namespace System.Windows.Forms
{
    public enum UnhandledExceptionMode { ThrowException }
    public enum MessageBoxButtons { OK }
    public enum MessageBoxIcon { Error }
    public static class Application
    {
        public static void SetUnhandledExceptionMode(UnhandledExceptionMode mode)
        {
            Tobtech.OmnikeyRco.StartupProbe.Record("exception-mode");
        }
    }
    public static class MessageBox
    {
        public static void Show(string message, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            throw new InvalidOperationException("Startup probe unexpectedly requested a dialog.");
        }
    }
}
'@
Add-Type -TypeDefinition ($entry + "`r`n" + $boundaries) -Language CSharp -ReferencedAssemblies @(
    'System.dll', 'System.Core.dll'
)

function Assert-Calls([string]$Expected, [string]$Name) {
    $actual = [string]::Join(',', [Tobtech.OmnikeyRco.StartupProbe]::Calls.ToArray())
    if ($actual -ne $Expected) { throw ($Name + ': unexpected calls: ' + $actual) }
    Write-Host ('PASS: ' + $Name)
}

[Tobtech.OmnikeyRco.StartupProbe]::Reset($null)
if ([Tobtech.OmnikeyRco.Entry]::Run($root, $false) -ne 0) { throw 'Normal startup failed.' }
Assert-Calls 'exception-mode,application' 'Normal startup reaches the application without regression tests or report I/O'

[Tobtech.OmnikeyRco.StartupProbe]::Reset($null)
if ([Tobtech.OmnikeyRco.Entry]::Run($root, $true) -ne 0) { throw 'Self-test startup failed.' }
Assert-Calls 'core,reception,reliability,data-path,report' 'Self-test runs every suite and writes a report without starting the application'
foreach ($count in @('11', '22', '33')) {
    if ([Tobtech.OmnikeyRco.StartupProbe]::Report -notmatch ('(?m)^' + $count + ' ')) {
        throw ('Self-test report omits suite count ' + $count)
    }
}

[Tobtech.OmnikeyRco.StartupProbe]::Reset('reception')
$originalError = [Console]::Error
$capturedError = New-Object IO.StringWriter
try {
    [Console]::SetError($capturedError)
    if ([Tobtech.OmnikeyRco.Entry]::Run($root, $true) -ne 1) { throw 'Failed self-test did not fail closed.' }
} finally {
    [Console]::SetError($originalError)
    $capturedError.Dispose()
}
Assert-Calls 'core,reception,data-path,error-report' 'A failing suite stops startup and preserves failure reporting'
Write-Host 'Startup routing verified. No application window or reader access was used.'
exit 0
