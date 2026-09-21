param([string]$OutputDirectory = 'outputs/燕雲兌換助手')
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$metadata = Join-Path $env:WINDIR 'System32/WinMetadata'
$refs = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll')
$refs += @('WindowsBase.dll','UIAutomationClient.dll','UIAutomationTypes.dll') | ForEach-Object { Join-Path "$framework/WPF" $_ }
$refs += Join-Path $framework 'System.Runtime.WindowsRuntime.dll'
$refs += Join-Path $framework 'System.Runtime.InteropServices.WindowsRuntime.dll'
$refs += (Get-ChildItem -LiteralPath "$env:WINDIR/Microsoft.NET/assembly/GAC_MSIL/System.Runtime" -Recurse -Filter System.Runtime.dll | Select-Object -First 1).FullName
$refs += (Get-ChildItem -LiteralPath "$env:WINDIR/Microsoft.NET/assembly/GAC_MSIL/System.Threading.Tasks" -Recurse -Filter System.Threading.Tasks.dll | Select-Object -First 1).FullName
$refs += @('Windows.Foundation.winmd','Windows.Globalization.winmd','Windows.Graphics.winmd','Windows.Media.winmd','Windows.Storage.winmd') | ForEach-Object { Join-Path $metadata $_ }
New-Item -ItemType Directory -Path "$projectRoot/$OutputDirectory" -Force | Out-Null
$arguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/utf8output',"/out:$projectRoot/$OutputDirectory/燕雲兌換助手.exe")
$arguments += $refs | ForEach-Object { '/reference:' + $_ }
$arguments += (Get-ChildItem -LiteralPath "$projectRoot/src" -Filter '*.cs').FullName
& "$framework/csc.exe" @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
