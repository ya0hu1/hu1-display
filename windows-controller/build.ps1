# Builds the original WPF Studio using Windows' existing .NET Framework compiler.
# If .ps1 execution is blocked, use the reviewed equivalent compiler command; do not bypass policy.
[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$fx=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$csc=Join-Path $fx 'csc.exe'
if(-not(Test-Path $csc)){throw 'Existing .NET Framework compiler missing; no automatic install.'}
if([string]::IsNullOrWhiteSpace($OutputDirectory)){$OutputDirectory=Join-Path $root 'bin'}
$out=Join-Path $OutputDirectory 'Hu1.Display.exe'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $out)|Out-Null
$files=@(Get-ChildItem (Join-Path $PSScriptRoot 'src/*.cs'),(Join-Path $PSScriptRoot 'tests/*.cs')|Where-Object { $_.Name -ne 'Ui.cs' }|Select-Object -ExpandProperty FullName)
$refs=@('System.dll','System.Management.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll',
 (Join-Path $fx 'System.Xaml.dll'),(Join-Path $fx 'WPF/WindowsBase.dll'),(Join-Path $fx 'WPF/PresentationCore.dll'),(Join-Path $fx 'WPF/PresentationFramework.dll'))
$flags=@(('/win32icon:'+(Join-Path $PSScriptRoot 'assets/desklink.ico')),'/nologo','/codepage:65001','/target:winexe',"/out:$out",('/resource:'+(Join-Path $PSScriptRoot 'src/Studio.xaml')+',DeskLink.Studio.xaml'),('/resource:'+(Join-Path $PSScriptRoot 'src/UiStrings.json')+',Huiying.UiStrings.json'))+@($refs|ForEach-Object {'/r:'+$_})
& $csc $flags $files
if($LASTEXITCODE -ne 0){throw 'Studio build failed.'}
