#requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$installer = Join-Path $root 'Program\Install-Update.ps1'
$temp = Join-Path ([IO.Path]::GetTempPath()) ('bricklasare-update-test-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temp)
$fixture = [IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'fixtures\signed-update-test.feed'))
$feedPath = Join-Path $temp 'release.feed'
$archive = Join-Path $temp 'package.zip'
$plan = Join-Path $temp 'plan.json'
$utf8 = [Text.UTF8Encoding]::new($false)
$script:checks = 0
function Assert-Installer([string]$Name, [bool]$Expected) {
    $out = & (Join-Path $PSHOME 'powershell.exe') -NoProfile -File $installer -Plan $plan -VerifyPackageOnly 2>&1
    if (($LASTEXITCODE -eq 0) -ne $Expected) { throw ($Name + ' failed: ' + ($out | Out-String)) }
    $script:checks++
    Write-Host ('PASS: ' + $Name)
}
# Execute the shipped snapshot functions, without running the installer entry point.
$tokens = $null; $parseErrors = $null
$syntax = [Management.Automation.Language.Parser]::ParseFile($installer, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw 'Installer syntax errors' }
foreach ($name in @('Assert-PlainPath','Assert-UpdateStorage','Get-UpdateSettingsSnapshot','Assert-UpdateSettingsSnapshot','Save-UpdateSettingsSnapshot')) {
    $function = @($syntax.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $true) | Where-Object Name -eq $name)
    if ($function.Count -ne 1) { throw 'Missing shipped snapshot function' }
    . ([scriptblock]::Create($function[0].Extent.Text))
}
function Assert-Snapshot([bool]$Condition, [string]$Description) {
    if (-not $Condition) { throw $Description }; $script:checks++; Write-Host ('PASS: ' + $Description)
}
function Reject-Snapshot([scriptblock]$Operation, [string]$Description) {
    $rejected = $false; try { & $Operation } catch { $rejected = $true }
    Assert-Snapshot $rejected $Description
}
try {
    $settings = Join-Path $temp 'data'; $jobDirectory = Join-Path $temp 'job'
    [void][IO.Directory]::CreateDirectory($settings); [void][IO.Directory]::CreateDirectory($jobDirectory)
    foreach ($name in @('aterstall-other.json','usb-pending-old.json','lasarljud-old.json')) {
        [IO.File]::WriteAllBytes((Join-Path $settings $name), [byte[]]@(0,255,17,0))
    }
    [IO.File]::WriteAllBytes((Join-Path $settings 'empty.JSON'), [byte[]]@())
    $saved = Save-UpdateSettingsSnapshot $settings $jobDirectory
    Assert-Snapshot ($saved.Count -eq 4) 'All opaque JSON settings including empty/corrupt records are copied'
    Assert-UpdateSettingsSnapshot $settings $saved
    Assert-UpdateSettingsSnapshot (Join-Path $jobDirectory 'preserved-settings') $saved
    Assert-Snapshot ($saved['empty.JSON'].Length -eq 0) 'Zero-length recovery files are preserved without interpretation'
    [IO.File]::WriteAllText((Join-Path $settings 'aterstall-other.json'), 'external edit')
    Reject-Snapshot { Assert-UpdateSettingsSnapshot $settings $saved } 'External edits abort installation without overwriting settings'
    Assert-UpdateSettingsSnapshot (Join-Path $jobDirectory 'preserved-settings') $saved
    Assert-Snapshot ([IO.File]::ReadAllText((Join-Path $settings 'aterstall-other.json')) -ceq 'external edit') 'Changed source and original backup are both retained'
    Reject-Snapshot { Save-UpdateSettingsSnapshot $settings $jobDirectory } 'An existing settings backup is never overwritten'
    $oversize = Join-Path $settings 'large.json'; [IO.File]::WriteAllBytes($oversize, (New-Object byte[] 65537))
    Reject-Snapshot { Get-UpdateSettingsSnapshot $settings } 'Every settings file has a strict size limit'
    [IO.File]::Delete($oversize)
    $invalid = Join-Path $settings 'directory.json'; [void][IO.Directory]::CreateDirectory($invalid)
    Reject-Snapshot { Get-UpdateSettingsSnapshot $settings } 'A JSON-named directory is rejected'
    [IO.Directory]::Delete($invalid)
    $many = Join-Path $temp 'many'; [void][IO.Directory]::CreateDirectory($many)
    for ($i=0; $i -lt 257; $i++) { [IO.File]::WriteAllBytes((Join-Path $many ($i.ToString()+'.json')), [byte[]]@()) }
    Reject-Snapshot { Get-UpdateSettingsSnapshot $many } 'The settings file count is bounded'
    $total = Join-Path $temp 'total'; [void][IO.Directory]::CreateDirectory($total)
    for ($i=0; $i -lt 33; $i++) { [IO.File]::WriteAllBytes((Join-Path $total ($i.ToString()+'.json')), (New-Object byte[] 65536)) }
    Reject-Snapshot { Get-UpdateSettingsSnapshot $total } 'The total settings copy size is bounded'
    Reject-Snapshot { Assert-UpdateStorage $temp $settings } 'Program replacement cannot enclose the data folder'
    Reject-Snapshot { Assert-UpdateStorage $settings $temp } 'Data cannot enclose the program folder'
    Reject-Snapshot { Assert-UpdateStorage $settings $settings } 'Program and data folders cannot be equal'
    Assert-UpdateStorage (Join-Path $temp 'data2') $settings
    Assert-Snapshot $true 'Path separation distinguishes actual folders from text prefixes'
    [IO.File]::WriteAllBytes($feedPath, $fixture)
    [IO.File]::WriteAllBytes($archive, [Text.Encoding]::UTF8.GetBytes('fixture'))
    [IO.File]::WriteAllText($plan, '{"Channel":"preview"}', $utf8)
    Assert-Installer 'Final installer accepts original signed fixture' $true
    [IO.File]::WriteAllText($plan, '{"Channel":"stable"}', $utf8)
    Assert-Installer 'Final installer rejects channel substitution' $false
    [IO.File]::WriteAllText($plan, '{"Channel":"preview","Sha256":"ignored","Size":123,"Version":"999.0.0"}', $utf8)
    Assert-Installer 'Unsigned plan metadata does not replace signed fields' $true
    [IO.File]::WriteAllBytes($archive, [Text.Encoding]::UTF8.GetBytes('changed'))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $changedHash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes('changed'))).Replace('-', '') } finally { $sha.Dispose() }
    [IO.File]::WriteAllText($plan, (@{Channel='preview';Sha256=$changedHash;Size=7;Version='99.1.0'} | ConvertTo-Json), $utf8)
    Assert-Installer 'Replacing both plan checksum and archive fails' $false
    [IO.File]::WriteAllBytes($archive, [Text.Encoding]::UTF8.GetBytes('fixture'))
    $changed = [byte[]]$fixture.Clone(); $changed[40] = $changed[40] -bxor 1
    [IO.File]::WriteAllBytes($feedPath, $changed)
    Assert-Installer 'Final installer rejects tampered signed payload' $false
    $lines = [Text.Encoding]::UTF8.GetString($fixture).TrimEnd([char[]]"`r`n").Split([char]"`n")
    $sig = [Convert]::FromBase64String($lines[2]); $sig[0] = $sig[0] -bxor 1
    $lines[2] = [Convert]::ToBase64String($sig)
    [IO.File]::WriteAllText($feedPath, ($lines -join "`n") + "`n", $utf8)
    Assert-Installer 'Final installer rejects tampered signature' $false
    [IO.File]::WriteAllText($feedPath, ('A' * 65537), $utf8)
    Assert-Installer 'Final installer bounds envelope reads' $false
    [IO.File]::WriteAllBytes($feedPath, $fixture)
    [IO.File]::WriteAllText($plan, ('A' * 16385), $utf8)
    Assert-Installer 'Final installer bounds plan reads' $false
    [IO.File]::WriteAllText($plan, '{"Channel":"preview"}', $utf8)
    Remove-Item -LiteralPath $feedPath
    Assert-Installer 'Final installer requires original signed envelope' $false
    Write-Host ('PASS: ' + $script:checks + ' installer security checks; no package code executed.')
} finally { Remove-Item -LiteralPath $temp -Recurse -Force }
# The final negative test intentionally leaves the child exit code at 1.
# Report this script's successful assertions instead of propagating that code to CI.
exit 0
