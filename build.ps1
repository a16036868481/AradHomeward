param([Parameter(Mandatory=$true)][string]$InstallRoot,[string]$ClientRuntime)
$ErrorActionPreference='Stop'
$taskInstall=[IO.Path]::GetFullPath($InstallRoot)
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskManifest=Join-Path $PSScriptRoot 'trainer\desktop\app.manifest'
$taskIcon=Join-Path $taskInstall 'DNF60_ACT12\ACT12\DNF.ico'
if(!(Test-Path -LiteralPath (Join-Path $taskInstall 'DNF60_ACT12\ACT12\LegacyLauncher.exe'))){throw 'InstallRoot must point to the root of a fully extracted flat release.'}
$taskCommon=@('/nologo','/target:winexe','/platform:anycpu','/define:PORTABLE',"/win32icon:$taskIcon","/win32manifest:$taskManifest",'/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll')
$taskPortable=Join-Path $PSScriptRoot 'setup\portable'
$taskProgramPaths=Join-Path $taskPortable 'InstallPrograms.cs'
$taskSources=@('Dnf60Control.cs','ClientExitMonitor.cs','LocalAccountWindow.cs','ControlLocks.cs','LauncherStartupMonitor.cs','LauncherMode.cs','LanguageSupport.cs','LanSession.cs','LanRoomWindow.cs','LanRoomText.cs','GameVolume.cs','GamePlaySettings.cs','GamePlayOptions.cs','GamepadInput.cs','GameWindow.cs') | ForEach-Object {Join-Path $PSScriptRoot "setup\$_"}
$taskSources+=Join-Path $taskPortable 'RelayPortPreflight.cs'
& $taskCompiler @taskCommon "/out:$taskInstall\AradHomeward.exe" @taskSources $taskProgramPaths
if($LASTEXITCODE-ne 0){throw 'Controller compilation failed.'}
Copy-Item -LiteralPath (Join-Path $taskInstall 'AradHomeward.exe') -Destination (Join-Path $taskInstall 'StopAll.exe')
$taskTrainerSources=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'trainer\desktop') -File -Filter '*.cs' | ForEach-Object {$_.FullName}
$taskTrainerSources+=Join-Path $PSScriptRoot 'setup\LanRoomText.cs'
& $taskCompiler @taskCommon "/out:$taskInstall\AradTrainer.exe" @taskTrainerSources $taskProgramPaths
if($LASTEXITCODE-ne 0){throw 'Trainer compilation failed.'}
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'trainer') -File -Filter '*.py' | Copy-Item -Destination (Join-Path $taskInstall 'trainer')
& $taskCompiler @taskCommon "/out:$taskInstall\CheckStartup.exe" "/resource:$taskPortable\startup_probe.py,startup_probe.py" (Join-Path $taskPortable 'StartupDiagnostics.cs') $taskProgramPaths
if($LASTEXITCODE-ne 0){throw 'Diagnostics compilation failed.'}
if($ClientRuntime){
    Copy-Item -LiteralPath $ClientRuntime -Destination (Join-Path $taskInstall 'DNF60_ACT12\ACT12\a12_live_score.dll')
}
$taskManifestPath=Join-Path $taskInstall 'localization\manifest.json'
$taskData=Get-Content -LiteralPath $taskManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
foreach($taskFile in @('阿拉德·归途.exe','备用关闭.exe','归途修改器.exe','DNF60控制台.exe','启动检查.exe','DNF60_ACT12/ACT12/开始游戏.exe')){
    $taskData.runtimeHashes.PSObject.Properties.Remove($taskFile)
}
foreach($taskFile in @('AradHomeward.exe','AradTrainer.exe','CheckStartup.exe','StopAll.exe','DNF60_ACT12/ACT12/LegacyLauncher.exe','trainer/lan_room.py','trainer/lan_guest.py','trainer/lan_network.py')){
    $taskData.runtimeHashes | Add-Member -NotePropertyName $taskFile -NotePropertyValue '' -Force
}
foreach($taskEntry in $taskData.runtimeHashes.PSObject.Properties){
    $taskFile=Join-Path $taskInstall $taskEntry.Name
    if(!(Test-Path -LiteralPath $taskFile)){throw "Missing language component: $($taskEntry.Name)"}
    $taskEntry.Value=(Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash.ToLowerInvariant()
}
[IO.File]::WriteAllText($taskManifestPath,($taskData | ConvertTo-Json -Depth 30),[Text.UTF8Encoding]::new($false))
Write-Output 'Portable Windows programs built and language hashes refreshed.'
