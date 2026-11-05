#requires -Version 5.1
[CmdletBinding()]
param([switch]$SelfTestOnly, [switch]$ConnectionReport)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
try {
    if ($PSVersionTable.PSEdition -ne 'Desktop') {
        throw 'Starta med Windows PowerShell 5.1 via Starta-Bricklasaren.cmd, inte PowerShell 7.'
    }
    if ($ExecutionContext.SessionState.LanguageMode -ne 'FullLanguage') {
        throw 'Organisationens PowerShell-policy tillåter inte programmets .NET-gränssnitt. Kontakta IT för godkännande enligt ordinarie rutin. Ändra inte säkerhetsinställningarna.'
    }
    if ([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne [System.Threading.ApartmentState]::STA) {
        throw 'Starta via Starta-Bricklasaren.cmd. Gränssnittet och urklippet behöver STA-läge.'
    }
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Add-Type -AssemblyName System.Management
    Add-Type -AssemblyName System.Web.Extensions

    $utf8 = New-Object System.Text.UTF8Encoding($true)
    $files = @('Entry.cs', 'OmnikeyCore.cs', 'ReaderSound.cs', 'ReaderNetwork.cs', 'ReaderNetworkTests.cs', 'ReceptionSupport.cs', 'ReaderSafety.cs', 'Appearance.cs', 'SettingsDialogs.cs', 'FirstStart.cs', 'Workflow.cs', 'Updates.cs', 'ReliabilityTests.cs', 'ReceptionApp.cs')
    $parts = foreach ($name in $files) {
        $path = Join-Path $PSScriptRoot $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Programfil saknas: $name. Packa upp hela ZIP-filen."
        }
        [System.IO.File]::ReadAllText($path, $utf8)
    }
    $source = $parts -join "`r`n"
    $references = @(
        'System.dll', 'System.Core.dll', [System.IO.Compression.ZipArchive].Assembly.Location, [System.IO.Compression.ZipFile].Assembly.Location,
        [System.Drawing.Color].Assembly.Location,
        [System.Windows.Forms.Form].Assembly.Location,
        [System.Web.Script.Serialization.JavaScriptSerializer].Assembly.Location, [System.Management.ManagementObjectSearcher].Assembly.Location
    )

    # Same hosting approach as v2: compile the readable C# sources with Add-Type
    # and run in the PowerShell process. No application EXE/output assembly is
    # requested, copied, or launched. No execution policy is changed here.
    Add-Type -TypeDefinition $source -ReferencedAssemblies $references -Language CSharp
    if ($ConnectionReport) {
        if ($SelfTestOnly) { throw 'Välj antingen anslutningskontroll eller självtest.' }
        $report = [Tobtech.OmnikeyRco.ReceptionFiles]::ConnectionReport($root)
        $reportPath = Join-Path ([Tobtech.OmnikeyRco.ReceptionFiles]::DataPath) 'anslutningskontroll.txt'
        [Tobtech.OmnikeyRco.ReceptionFiles]::WriteAtomic($reportPath, $report)
        Write-Host $report
        Write-Host ('Rapporten är sparad: ' + $reportPath)
        exit 0
    }
    $result = [Tobtech.OmnikeyRco.Entry]::Run($root, $SelfTestOnly.IsPresent)
    if ($SelfTestOnly -and $result -eq 0) {
        $report = Join-Path ([Tobtech.OmnikeyRco.ReceptionFiles]::DataPath) 'sjalvtest-reception.txt'
        Write-Host ([System.IO.File]::ReadAllText($report, $utf8))
    }
    exit $result
}
catch {
    $message = $_.Exception.Message
    $details = $_ | Out-String
    $safeLog = 'Startfel: {0}; HRESULT 0x{1:X8}' -f $_.Exception.GetType().FullName, $_.Exception.HResult
    $logPath = ''
    try {
        $data = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Tobtech\Omnikey-RCO-v2'
        [System.IO.Directory]::CreateDirectory($data) | Out-Null
        $logPath = Join-Path $data 'startfel-reception.txt'
        [System.IO.File]::WriteAllText($logPath, $safeLog, (New-Object System.Text.UTF8Encoding($true)))
    } catch { }
    Write-Host $details -ForegroundColor Red
    try {
        [void][System.Windows.Forms.MessageBox]::Show(
            "Brickläsaren kunde inte starta. Kontakta IT.`r`n`r`n$message`r`n`r`nFellogg: $logPath",
            'Brickläsaren', [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error)
    } catch { }
    exit 1
}
