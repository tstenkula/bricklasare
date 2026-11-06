#requires -Version 5.1
[CmdletBinding()]
param([string]$Address)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$phase = 'START'
try {
    if ($PSVersionTable.PSEdition -ne 'Desktop' -or [Environment]::OSVersion.Platform -ne 'Win32NT') {
        throw 'Verktyget kräver Windows PowerShell 5.1.'
    }
    Write-Host 'BRICKLÄSAREN – UNDERLAG FÖR LÄSARLJUD'
    Write-Host 'Stäng Brickläsaren och ta bort alla brickor från läsaren.'
    Write-Host 'Verktyget läser bara webbgränssnittets kod via USB. Inga inställningar ändras.'
    Write-Host 'Skript kan innehålla lokala uppgifter. Granska underlaget innan det delas.'
    [void](Read-Host 'Tryck Enter för att börja, eller stäng fönstret för att avbryta')
    $base = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Tobtech\Omnikey-RCO-v2'
    $directory = Join-Path $base ('ljudunderlag-2-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
    if (-not $PSBoundParameters.ContainsKey('Address')) {
        $Address = '192.168.63.99'
        $config = Join-Path $base 'reception.json'
        if ((Test-Path -LiteralPath $config -PathType Leaf) -and (Get-Item -LiteralPath $config).Length -le 16384) {
            try {
                $saved = [IO.File]::ReadAllText($config) | ConvertFrom-Json
                if ($null -ne $saved.Address -and $saved.Address -is [string]) { $Address = $saved.Address }
            } catch { Write-Host 'Den sparade adressen kunde inte läsas; standardadressen används.' }
        }
    }
    $phase = 'COMPILE_TOOL'
    $source = @('ReaderNetwork.cs', 'ReaderWebFiles.cs', 'ReaderSoundInspection.cs') | ForEach-Object {
        [IO.File]::ReadAllText((Join-Path $PSScriptRoot $_))
    }
    Add-Type -TypeDefinition ($source -join "`r`n") -ReferencedAssemblies @('System.dll', 'System.Core.dll', 'System.Management.dll') -Language CSharp
    $phase = 'COLLECT'
    $report = [Tobtech.OmnikeyRco.ReaderSoundInspection]::Run($Address, $directory)
    $phase = 'CREATE_ZIP'
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
    Write-Host 'Stoppet kan gälla webbgränssnittet även om Brickläsaren kan ansluta.'
    $failure = $_.Exception.GetBaseException()
    Write-Host ('Feltyp: ' + $failure.GetType().FullName)
    Write-Host ('Arbetssteg: ' + $phase)
    if (Get-Variable directory -ErrorAction SilentlyContinue) {
        try {
            [void][IO.Directory]::CreateDirectory($directory)
            [IO.File]::WriteAllText((Join-Path $directory 'verktygsfel.txt'), ('Steg: ' + $phase + "`r`nFeltyp: " + $failure.GetType().FullName + "`r`n"), (New-Object Text.UTF8Encoding($false)))
            $control = Join-Path $directory 'kontroll.txt'
            if (Test-Path -LiteralPath $control) { Get-Content -LiteralPath $control -Encoding UTF8 | Select-Object -Last 8 | ForEach-Object { Write-Host $_ } }
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $zip = $directory + '.zip'
            if (Test-Path -LiteralPath $zip) { [IO.File]::Delete($zip) }
            [IO.Compression.ZipFile]::CreateFromDirectory($directory, $zip)
            Write-Host ('Underlag: ' + $zip)
        } catch { Write-Host 'ZIP eller kontrollrapport kunde inte skrivas. Kontrollera skrivbehörighet och ledigt utrymme.' }
        Write-Host ('Rapportmapp: ' + $directory)
    }
    exit 1
}
