param([string]$OutputPath = 'F:\dnf\DNF60控制台.next.exe', [switch]$Portable)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskCompiler = Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskSources = @((Join-Path $PSScriptRoot 'Dnf60Control.cs'), (Join-Path $PSScriptRoot 'ClientExitMonitor.cs'), (Join-Path $PSScriptRoot 'LocalAccountWindow.cs'), (Join-Path $PSScriptRoot 'ControlLocks.cs'), (Join-Path $PSScriptRoot 'LauncherStartupMonitor.cs'), (Join-Path $PSScriptRoot 'LauncherMode.cs'))
$taskManifest = Join-Path $taskRoot 'trainer\desktop\app.manifest'
$taskSources += Join-Path $PSScriptRoot 'LanguageSupport.cs'
$taskSources += Join-Path $PSScriptRoot 'portable\RelayPortPreflight.cs'
$taskIcon = Join-Path $taskRoot 'DNF60_ACT12\ACT12\DNF.ico'
$taskDefines = @()
if ($Portable) { $taskDefines += '/define:PORTABLE' }
& $taskCompiler /nologo /target:winexe /platform:anycpu "/out:$OutputPath" "/win32icon:$taskIcon" "/win32manifest:$taskManifest" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll @taskDefines $taskSources
if ($LASTEXITCODE -ne 0) { throw '会话控制器编译失败。' }
$taskBuildInfo = @{version='2.6.0.23';portable=[bool]$Portable;output=[IO.Path]::GetFullPath($OutputPath);sha256=(Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant();sources=@{}}
foreach($taskSource in $taskSources){$taskBuildInfo.sources[[IO.Path]::GetFileName($taskSource)]=(Get-FileHash -LiteralPath $taskSource -Algorithm SHA256).Hash.ToLowerInvariant()}
[IO.File]::WriteAllText($OutputPath+'.build.json',($taskBuildInfo | ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256
