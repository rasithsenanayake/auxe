$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$root = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\Auxe'
if ($PSScriptRoot -ne $root) { throw 'Run the installed Auxe uninstaller from Windows Settings.' }
if ([Windows.Forms.MessageBox]::Show('Uninstall Auxe?', 'Auxe', 'OKCancel', 'Question') -ne 'OK') { exit }
$exe = Join-Path $root 'Auxe.exe'
Get-Process Auxe -ErrorAction SilentlyContinue | Where-Object Path -eq $exe | Stop-Process
$hostKey = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.auxe.companion'
if (Test-Path -LiteralPath $hostKey) {
    if ((Get-Item -LiteralPath $hostKey).GetValue('') -eq (Join-Path $root 'chrome-host.json')) {
        Remove-Item -LiteralPath $hostKey
    }
}
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Auxe'
if (Test-Path -LiteralPath $uninstallKey) { Remove-Item -LiteralPath $uninstallKey }
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Auxe.lnk'
if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut }
$resolved = (Resolve-Path -LiteralPath $root).Path
if ($resolved -ne $root -or ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'The install folder could not be verified.'
}
$chrome = Join-Path $resolved 'chrome'
foreach ($name in 'manifest.json','worker.js','music.js','playback.html','playback.js','devices.html','devices.js') {
    $file = Join-Path $chrome $name
    if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file }
}
if ((Test-Path -LiteralPath $chrome) -and -not (Get-ChildItem -LiteralPath $chrome -Force)) { Remove-Item -LiteralPath $chrome }
foreach ($name in 'Auxe.exe','chrome-host.json','uninstall.ps1') {
    $file = Join-Path $resolved $name
    if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file }
}
if (-not (Get-ChildItem -LiteralPath $resolved -Force)) { Remove-Item -LiteralPath $resolved }
