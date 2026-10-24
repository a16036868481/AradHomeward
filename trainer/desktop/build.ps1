param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$taskCompiler = Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskOutput = if($OutputPath){[IO.Path]::GetFullPath($OutputPath)}else{Join-Path $taskRoot '归途修改器.exe'}
if(-not $taskOutput.StartsWith($taskRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Build output must stay inside this installation.'}
$taskSources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | ForEach-Object { $_.FullName })
$taskManifest = Join-Path $PSScriptRoot 'app.manifest'
$taskIcon = Join-Path $taskRoot 'DNF60_ACT12\ACT12\DNF.ico'
if (Get-Process | Where-Object { try { $_.Path -eq $taskOutput } catch { $false } }) {
    throw '请先关闭桌面修改器窗口，再编译。游戏无需关闭。'
}
& $taskCompiler /nologo /target:winexe /platform:anycpu "/out:$taskOutput" "/win32icon:$taskIcon" "/win32manifest:$taskManifest" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll $taskSources
if ($LASTEXITCODE -ne 0) { throw '桌面修改器编译失败。' }
Get-FileHash -LiteralPath $taskOutput -Algorithm SHA256
