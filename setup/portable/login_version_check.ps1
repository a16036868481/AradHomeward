param([string]$ServiceDirectory,[string]$ConfigFile)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)
$OutputEncoding=[Console]::OutputEncoding
function Verify-LoginFiles([string]$directory,$expected) {
    Write-Host ('检查目录：'+$directory)
    foreach($name in @('a12_registration_server.exe','a12_sqlite3.dll')) {
        $entry=$expected.PSObject.Properties[$name]
        if(!$entry -or $entry.Value -notmatch '^[a-f0-9]{64}$'){throw ('缺少有效校验值：'+$name)}
        $file=Join-Path $directory $name
        if(!(Test-Path -LiteralPath $file -PathType Leaf)){throw ('缺少文件：'+$name)}
        $hash=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
        Write-Host ($name+' SHA256='+$hash)
        if($hash -ine $entry.Value){throw ('文件版本不一致：'+$name)}
    }
}
try {
    $manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'login_version_manifest.json') -Encoding UTF8 -Raw | ConvertFrom-Json
    $folders=[ordered]@{'x64'='login_service';'x64-compat'='login_service_x64_compat'}
    if (!$ServiceDirectory) {
        if($manifest.x64){Verify-LoginFiles (Join-Path $PSScriptRoot $folders.x64) $manifest.x64}
        Verify-LoginFiles (Join-Path $PSScriptRoot $folders.'x64-compat') $manifest.'x64-compat'
        $root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
        $report=& (Join-Path $root 'runtime\python\python.exe') -I -B (Join-Path $root 'trainer\login_service.py') runtime | ConvertFrom-Json
        if(!$report.ok){throw ('['+$report.code+'] '+$report.message)}
        if($report.backend -notin $folders.Values){throw '登录服务入口无效。'}
        $directory=Join-Path $PSScriptRoot $report.backend
        $exe=Join-Path $directory 'a12_registration_server.exe'
        $runtime=$report.runtime
    } else {
        $directory=(Resolve-Path -LiteralPath $ServiceDirectory).Path
        $exe=Join-Path $directory 'a12_registration_server.exe'
        if(!(Test-Path -LiteralPath $exe -PathType Leaf)){throw '该目录缺少登录服务程序。'}
        $actualExe=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
        $expected=$null
        foreach($variant in $folders.Keys) {
            if($manifest.$variant.'a12_registration_server.exe' -ieq $actualExe){$expected=$manifest.$variant;break}
        }
        if(!$expected){throw '登录服务EXE与当前更新包不一致，请使用更新包中的完整登录服务。'}
        Verify-LoginFiles $directory $expected
        $info=& $exe --runtime-info
        if($LASTEXITCODE -ne 0){throw '运行库检查失败，请查看上面的错误。'}
        $runtime=$info | ConvertFrom-Json
    }
    if($runtime.Bitness -ne 64){throw '登录服务未报告 64 位运行环境。'}
    Write-Host ('实际登录服务入口：'+$directory)
    Write-Host ('进程位数：'+$runtime.Bitness+'位；SQLite：'+$runtime.SqliteVersion+'；TLS：'+$runtime.TlsProtocols)
    if(!$ConfigFile) {
        $ConfigFile=Join-Path $directory 'registration.json'
        if(!(Test-Path -LiteralPath $ConfigFile -PathType Leaf)){$ConfigFile=Join-Path $PSScriptRoot 'login_service/registration.json'}
    }
    if(!(Test-Path -LiteralPath $ConfigFile -PathType Leaf)){throw '缺少现有registration.json，请将更新合并到原服务端目录。'}
    Write-Host '程序版本通过检查，执行只读账号表检查：'
    & $exe --check --config $ConfigFile
    if($LASTEXITCODE -ne 0){throw '账号库检查失败，请按实际路径和错误处理。'}
    Write-Host '检查通过。'
    exit 0
}catch {
    Write-Host ('检查失败：'+$_.Exception.Message)
    exit 1
}
