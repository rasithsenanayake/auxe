$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force bin | Out-Null
& $compiler /nologo /optimize+ /platform:x64 /out:bin/IconBuild.exe /reference:System.Drawing.dll SpeakerArt.cs IconBuild.cs
if ($LASTEXITCODE -ne 0) { throw 'Auxe icon compiler failed.' }
& ./bin/IconBuild.exe bin/Auxe.ico
if ($LASTEXITCODE -ne 0) { throw 'Auxe icon generation failed.' }
$fonts = @('/resource:assets/Geist-Medium.ttf,Geist.ttf', '/resource:assets/Poppins-SemiBold.ttf,Poppins.ttf', '/resource:assets/Geist-OFL.txt,Geist-OFL.txt', '/resource:assets/Poppins-OFL.txt,Poppins-OFL.txt')
& $compiler /nologo /optimize+ /platform:x64 /target:winexe /win32manifest:Auxe.manifest /win32icon:bin/Auxe.ico /out:bin/Auxe.exe @fonts /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll Audio.cs Tray.cs VolumeSlider.cs ChromeBridge.cs Updates.cs SpeakerArt.cs Typography.cs Startup.cs Theme.cs ProductInfo.cs
if ($LASTEXITCODE -ne 0) { throw 'Auxe compilation failed.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = Join-Path $PSScriptRoot 'bin\chrome.zip'
if ([IO.File]::Exists($archive)) { [IO.File]::Delete($archive) }
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $PSScriptRoot chrome), $archive)
& $compiler /nologo /optimize+ /platform:x64 /target:winexe /win32icon:bin/Auxe.ico /out:bin/AuxeSetup.exe @fonts /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /resource:bin/Auxe.exe,app /resource:bin/chrome.zip,chrome /resource:uninstall.ps1,uninstall Setup.cs SpeakerArt.cs Typography.cs Startup.cs Theme.cs ProductInfo.cs
if ($LASTEXITCODE -ne 0) { throw 'Auxe Setup compilation failed.' }
