param([Parameter(Mandatory=$true)][string]$InstallRoot,[string]$ClientRuntime,[switch]$Windows7Runtime,[string]$LoginBackends)
$ErrorActionPreference='Stop'
$taskInstall=[IO.Path]::GetFullPath($InstallRoot)
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskManifest=Join-Path $PSScriptRoot 'trainer\desktop\app.manifest'
$taskIcon=Join-Path $taskInstall 'DNF60_ACT12\ACT12\DNF.ico'
if(!(Test-Path -LiteralPath (Join-Path $taskInstall 'DNF60_ACT12\ACT12\LegacyLauncher.exe'))){throw 'InstallRoot must point to the root of a fully extracted flat release.'}
if($Windows7Runtime -and !(Test-Path -LiteralPath (Join-Path $taskInstall 'runtime\python\python38._pth'))){
    throw 'Stage the Windows 7 Python runtime before building this variant.'
}
& (Join-Path $taskInstall 'runtime\python\python.exe') -B (Join-Path $PSScriptRoot 'setup\localization\repair_shared_sprites.py') $taskInstall
if($LASTEXITCODE-ne 0){throw 'Shared sprite build failed.'}
& (Join-Path $taskInstall 'runtime\python\python.exe') -B (Join-Path $PSScriptRoot 'setup\portable\fix_population_settings.py') $taskInstall
if($LASTEXITCODE-ne 0){throw 'ACT12 server patch failed.'}
$taskDefine=if($Windows7Runtime){'/define:PORTABLE,WINDOWS7_RUNTIME'}else{'/define:PORTABLE'}
$taskCommon=@('/nologo','/target:winexe','/platform:anycpu',$taskDefine,"/win32icon:$taskIcon","/win32manifest:$taskManifest",'/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll')
$taskPortable=Join-Path $PSScriptRoot 'setup\portable'
$taskProgramPaths=Join-Path $taskPortable 'InstallPrograms.cs'
$taskServer=Join-Path $taskInstall 'DNF60_ACT12\ACT12_Server'
$taskServiceEntry=Join-Path $taskServer 'a12_compat_launcher.exe'
$taskVendorEntry=Join-Path $taskServer 'a12_compat_vendor.exe'
if(!(Test-Path -LiteralPath $taskVendorEntry)){
    if((Get-Item -LiteralPath $taskServiceEntry).VersionInfo.FileDescription -eq 'Arad Homeward service entry'){throw 'Missing original diagnostic launcher.'}
    Copy-Item -LiteralPath $taskServiceEntry -Destination $taskVendorEntry
}
& $taskCompiler @taskCommon "/out:$taskServiceEntry" (Join-Path $taskPortable 'LegacyServiceEntry.cs')
if($LASTEXITCODE-ne 0){throw 'Service entry compilation failed.'}
foreach($taskCheck in @('win10_compat_check.ps1','login_version_check.ps1')){
    $script=[IO.File]::ReadAllText((Join-Path $taskPortable $taskCheck))
    [IO.File]::WriteAllText((Join-Path $taskServer $taskCheck),$script,[Text.UTF8Encoding]::new($true))
}
if($LoginBackends){
    $loginHashes=@{}
    $variants=if($Windows7Runtime){@('x64-compat')}else{@('x64','x64-compat')}
    foreach($variant in $variants){
        $folder=if($variant -eq 'x64'){'login_service'}else{'login_service_x64_compat'}
        $loginDir=Join-Path $taskServer $folder
        $backendExe=Join-Path $LoginBackends "$variant\a12_registration_server.exe"
        if(Test-Path -LiteralPath $backendExe -PathType Leaf){
            [IO.Directory]::CreateDirectory($loginDir) | Out-Null
            Copy-Item -LiteralPath $backendExe -Destination $loginDir
        }
        $sqlite=Join-Path $loginDir 'a12_sqlite3.dll'
        if(!(Test-Path -LiteralPath $sqlite -PathType Leaf) -and $variant -eq 'x64-compat'){
            Copy-Item -LiteralPath (Join-Path $taskServer 'login_service\a12_sqlite3.dll') -Destination $sqlite
        }
        $hashes=@{}
        foreach($file in @('a12_registration_server.exe','a12_sqlite3.dll')){$hashes[$file]=(Get-FileHash -LiteralPath (Join-Path $loginDir $file) -Algorithm SHA256).Hash}
        $loginHashes[$variant]=$hashes
    }
    [IO.File]::WriteAllText((Join-Path $taskServer 'login_version_manifest.json'),($loginHashes | ConvertTo-Json -Depth 3),[Text.UTF8Encoding]::new($false))
}
$taskSources=@('Dnf60Control.cs','ClientExitMonitor.cs','LocalAccountWindow.cs','ControlLocks.cs','LauncherStartupMonitor.cs','LauncherMode.cs','LanguageSupport.cs','LanSession.cs','LanRoomWindow.cs','LanRoomText.cs','GameVolume.cs','GamePlaySettings.cs','GamePreferencesBridge.cs','GamepadInput.cs','GameWindow.cs') | ForEach-Object {Join-Path $PSScriptRoot "setup\$_"}
$taskSources+=Join-Path $taskPortable 'RelayPortPreflight.cs'
& $taskCompiler @taskCommon "/out:$taskInstall\AradHomeward.exe" @taskSources $taskProgramPaths
if($LASTEXITCODE-ne 0){throw 'Controller compilation failed.'}
Copy-Item -LiteralPath (Join-Path $taskInstall 'AradHomeward.exe') -Destination (Join-Path $taskInstall 'StopAll.exe')
$taskTrainerSources=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'trainer\desktop') -File -Filter '*.cs' | ForEach-Object {$_.FullName}
$taskTrainerSources+=Join-Path $PSScriptRoot 'setup\LanRoomText.cs'
$taskTrainerSources+=Join-Path $PSScriptRoot 'setup\GamePlaySettings.cs'
& $taskCompiler @taskCommon "/out:$taskInstall\AradTrainer.exe" @taskTrainerSources $taskProgramPaths
if($LASTEXITCODE-ne 0){throw 'Trainer compilation failed.'}
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'trainer') -File -Filter '*.py' | Copy-Item -Destination (Join-Path $taskInstall 'trainer')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'trainer\item-grant-rules.json') -Destination (Join-Path $taskInstall 'trainer')
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
foreach($taskFile in @('AradHomeward.exe','AradTrainer.exe','CheckStartup.exe','StopAll.exe','DNF60_ACT12/ACT12/LegacyLauncher.exe','trainer/lan_room.py','trainer/lan_guest.py','trainer/lan_network.py','trainer/runtime_compat.py','trainer/login_service.py','trainer/input_scene.py','trainer/item_grants.py','trainer/item-grant-rules.json')){
    $taskData.runtimeHashes | Add-Member -NotePropertyName $taskFile -NotePropertyValue '' -Force
}
foreach($taskPatch in @('manifest.json','hud-button-rims.img','hud-menu-backing.img','minimap-heading.img') + @(0..3 | ForEach-Object {"lorien-floor-$_.img"})){
    $taskData.runtimeHashes | Add-Member -NotePropertyName "localization/sprites/$taskPatch" -NotePropertyValue '' -Force
}
foreach($taskEntry in $taskData.runtimeHashes.PSObject.Properties){
    $taskFile=Join-Path $taskInstall $taskEntry.Name
    if(!(Test-Path -LiteralPath $taskFile)){throw "Missing language component: $($taskEntry.Name)"}
    $taskEntry.Value=(Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash.ToLowerInvariant()
}
[IO.File]::WriteAllText($taskManifestPath,($taskData | ConvertTo-Json -Depth 30),[Text.UTF8Encoding]::new($false))
Write-Output 'Portable Windows programs built and language hashes refreshed.'
