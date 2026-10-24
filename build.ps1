param([Parameter(Mandatory=$true)][string]$InstallRoot)
$ErrorActionPreference='Stop'
$taskInstall=[IO.Path]::GetFullPath($InstallRoot)
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskManifest=Join-Path $PSScriptRoot 'trainer\desktop\app.manifest'
$taskIcon=Join-Path $taskInstall 'DNF60_ACT12\ACT12\DNF.ico'
if(!(Test-Path -LiteralPath $taskIcon)){throw 'InstallRoot must point to the app folder of a fully extracted release.'}
$taskCommon=@('/nologo','/target:winexe','/platform:anycpu',"/win32icon:$taskIcon","/win32manifest:$taskManifest",'/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll')
$taskSources=@('Dnf60Control.cs','ClientExitMonitor.cs','LocalAccountWindow.cs','ControlLocks.cs','LauncherStartupMonitor.cs','LauncherMode.cs','LanguageSupport.cs') | ForEach-Object {Join-Path $PSScriptRoot "setup\$_"}
$taskSources+=Join-Path $PSScriptRoot 'setup\portable\RelayPortPreflight.cs'
& $taskCompiler @taskCommon /define:PORTABLE "/out:$taskInstall\DNF60控制台.exe" @taskSources
if($LASTEXITCODE-ne 0){throw 'Controller compilation failed.'}
$taskTrainerSources=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'trainer\desktop') -File -Filter '*.cs' | ForEach-Object {$_.FullName}
& $taskCompiler @taskCommon "/out:$taskInstall\归途修改器.exe" @taskTrainerSources
if($LASTEXITCODE-ne 0){throw 'Trainer compilation failed.'}
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'trainer') -File -Filter '*.py' | Copy-Item -Destination (Join-Path $taskInstall 'trainer')
$taskPublicRoot=Split-Path -Parent $taskInstall
& $taskCompiler @taskCommon "/out:$taskPublicRoot\AradHomeward.exe" (Join-Path $PSScriptRoot 'setup\portable\PublicEntry.cs')
if($LASTEXITCODE-ne 0){throw 'Entry compilation failed.'}
foreach($taskName in @('AradTrainer.exe','StopAll.exe','CheckStartup.exe')){Copy-Item -LiteralPath (Join-Path $taskPublicRoot 'AradHomeward.exe') -Destination (Join-Path $taskPublicRoot $taskName)}
$taskPortable=Join-Path $PSScriptRoot 'setup\portable'
& $taskCompiler @taskCommon "/out:$taskInstall\启动检查.exe" "/resource:$taskPortable\startup_probe.py,startup_probe.py" (Join-Path $taskPortable 'StartupDiagnostics.cs')
if($LASTEXITCODE-ne 0){throw 'Diagnostics compilation failed.'}
$taskManifestPath=Join-Path $taskInstall 'localization\manifest.json'
$taskData=Get-Content -LiteralPath $taskManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
foreach($taskEntry in $taskData.runtimeHashes.PSObject.Properties){
    $taskFile=Join-Path $taskInstall $taskEntry.Name
    if(!(Test-Path -LiteralPath $taskFile)){throw "Missing language component: $($taskEntry.Name)"}
    $taskEntry.Value=(Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash.ToLowerInvariant()
}
[IO.File]::WriteAllText($taskManifestPath,($taskData | ConvertTo-Json -Depth 30),[Text.UTF8Encoding]::new($false))
Write-Output 'Portable Windows entries, controller, trainer and diagnostics built; language hashes refreshed.'
