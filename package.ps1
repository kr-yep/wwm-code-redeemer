$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
& (Join-Path $projectRoot 'build.ps1')
$appFolder = Join-Path $projectRoot 'outputs/燕雲兌換助手'
$appExe = Join-Path $appFolder '燕雲兌換助手.exe'
$instructions = Join-Path $appFolder '使用說明.txt'
Copy-Item -LiteralPath (Join-Path $projectRoot 'USAGE.md') -Destination $instructions -Force
$zipPath = Join-Path $projectRoot 'outputs/燕雲兌換助手.zip'
# Explicit allowlist: never package a user's data, account, screenshots, or logs.
Compress-Archive -LiteralPath @($appExe, $instructions) -DestinationPath $zipPath -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($archive.Entries | ForEach-Object FullName | Sort-Object)
    $expected = @('燕雲兌換助手.exe', '使用說明.txt') | Sort-Object
    if (@(Compare-Object $expected $entries).Count -ne 0) { throw 'Unexpected package contents.' }
} finally { $archive.Dispose() }
Write-Output $zipPath
