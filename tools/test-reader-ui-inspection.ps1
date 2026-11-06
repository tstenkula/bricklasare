#requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$paths = @('Program/ReaderNetwork.cs', 'tools/reader-ui-inspection/ReaderWebFiles.cs', 'tools/reader-ui-inspection/ReaderSoundInspection.cs', 'tools/reader-ui-inspection/Tests.cs')
$source = $paths | ForEach-Object { [IO.File]::ReadAllText((Join-Path $root $_)) }
$parameters = New-Object CodeDom.Compiler.CompilerParameters
$parameters.CompilerOptions = '/langversion:5 /warnaserror'
foreach ($assembly in @('System.dll','System.Core.dll','System.Management.dll','System.IO.Compression.dll')) { [void]$parameters.ReferencedAssemblies.Add($assembly) }
Add-Type -TypeDefinition ($source -join "`r`n") -CompilerParameters $parameters -Language CSharp
[void][Tobtech.OmnikeyRco.InspectionTests]::Run()
foreach ($path in @('tools/reader-ui-inspection/Samla-ljudunderlag.ps1', 'tools/build-reader-ui-inspection.ps1')) {
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile((Join-Path $root $path), [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw 'Invalid inspection PowerShell script.' }
}
