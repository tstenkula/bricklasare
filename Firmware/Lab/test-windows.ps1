#requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSEdition -ne 'Desktop' -or [Environment]::OSVersion.Platform -ne 'Win32NT') {
    throw 'Use Windows PowerShell 5.1 for the C# 5 / .NET Framework lab.'
}
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Firmware.cs')) + "`r`n" +
    [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Tests.cs'))
$parameters = New-Object CodeDom.Compiler.CompilerParameters
$parameters.CompilerOptions = '/langversion:5'
$parameters.ReferencedAssemblies.Add('System.dll') | Out-Null
$parameters.ReferencedAssemblies.Add('System.Core.dll') | Out-Null
Add-Type -TypeDefinition $source -Language CSharp -CompilerParameters $parameters
$groups = [Tobtech.FirmwareLab.FirmwareLabTests]::Run()
if ($groups -ne 10) { throw 'Missing firmware lab safety test groups.' }
