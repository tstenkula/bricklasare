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
try {
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
