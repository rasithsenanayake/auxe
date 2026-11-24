$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force bin | Out-Null
& $compiler /nologo /optimize+ /platform:x64 /target:winexe /out:bin/Soundmix.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll Audio.cs Tray.cs ChromeBridge.cs
if ($LASTEXITCODE -ne 0) { throw 'Soundmix compilation failed.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = Join-Path $PSScriptRoot 'bin\chrome.zip'
if ([IO.File]::Exists($archive)) { [IO.File]::Delete($archive) }
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $PSScriptRoot chrome), $archive)
& $compiler /nologo /optimize+ /platform:x64 /target:winexe /out:bin/SoundmixSetup.exe /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /resource:bin/Soundmix.exe,app /resource:bin/chrome.zip,chrome /resource:uninstall.ps1,uninstall Setup.cs
if ($LASTEXITCODE -ne 0) { throw 'Soundmix Setup compilation failed.' }
