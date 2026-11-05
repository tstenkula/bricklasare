#requires -Version 5.1
[CmdletBinding()]
param([string]$Address = '192.168.63.99')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
try {
    if ($PSVersionTable.PSEdition -ne 'Desktop' -or [Environment]::OSVersion.Platform -ne 'Win32NT') {
        throw 'Verktyget kräver Windows PowerShell 5.1.'
    }
    Write-Host 'BRICKLÄSAREN – UNDERLAG FÖR LÄSARLJUD'
    Write-Host 'Stäng Brickläsaren och ta bort alla brickor från läsaren.'
    Write-Host 'Verktyget läser bara webbgränssnittets kod via USB. Inga inställningar ändras.'
    Write-Host 'Skript kan innehålla lokala uppgifter. Granska underlaget innan det delas.'
    [void](Read-Host 'Tryck Enter för att börja, eller stäng fönstret för att avbryta')
    $source = @('ReaderNetwork.cs', 'ReaderWebFiles.cs', 'ReaderSoundInspection.cs') | ForEach-Object {
        [IO.File]::ReadAllText((Join-Path $PSScriptRoot $_))
    }
    Add-Type -TypeDefinition ($source -join "`r`n") -ReferencedAssemblies @('System.dll', 'System.Core.dll', 'System.Management.dll') -Language CSharp
    $base = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Tobtech\Omnikey-RCO-v2'
    $directory = Join-Path $base ('ljudunderlag-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
    $report = [Tobtech.OmnikeyRco.ReaderSoundInspection]::Run($Address, $directory)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = $directory + '.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($directory, $zip)
    Write-Host ''
    Write-Host 'Insamlingen är klar. Detta bekräftar inte att appens ljudstyrning fungerar ännu.'
    Write-Host ('Underlag: ' + $zip)
    Write-Host 'Ingenting har laddats upp automatiskt.'
    exit 0
} catch {
    Write-Host ''
    Write-Host 'Underlaget kunde inte samlas in. Inga skrivkommandon har skickats till läsaren.'
    Write-Host 'Kontrollera att en enda OMNIKEY 5427 CK är ansluten och att EEM-drivrutinen finns.'
    $failure = $_.Exception.GetBaseException()
    Write-Host ('Feltyp: ' + $failure.GetType().FullName)
    if (Get-Variable directory -ErrorAction SilentlyContinue) { Write-Host ('Eventuellt delunderlag: ' + $directory) }
    exit 1
}
