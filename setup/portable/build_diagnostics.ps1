param([Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$taskCompiler=Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $taskCompiler /nologo /target:winexe /platform:anycpu "/out:$OutputPath" "/win32icon:$taskRoot\DNF60_ACT12\ACT12\DNF.ico" "/win32manifest:$taskRoot\trainer\desktop\app.manifest" /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll "/resource:$PSScriptRoot\startup_probe.py,startup_probe.py" (Join-Path $PSScriptRoot 'StartupDiagnostics.cs')
if($LASTEXITCODE -ne 0){throw '启动检查编译失败。'}
Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256
