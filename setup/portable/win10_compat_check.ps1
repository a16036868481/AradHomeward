param([string]$ReportFile)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)
$OutputEncoding=[Console]::OutputEncoding
if(!$ReportFile){$ReportFile=Join-Path $PSScriptRoot 'Win10兼容检查结果.txt'}
$lines=New-Object System.Collections.Generic.List[string]
function Add-Line([string]$text){$lines.Add($text);Write-Host $text}
function Image-Machine([string]$path){
    $bytes=[IO.File]::ReadAllBytes($path)
    if($bytes.Length -lt 64){return '未知'}
    $pe=[BitConverter]::ToInt32($bytes,60)
    if($pe -lt 0 -or $pe+6 -gt $bytes.Length){return '未知'}
    switch([BitConverter]::ToUInt16($bytes,$pe+4)){0x014c{'32位'};0x8664{'64位'};default{'其他架构'}}
}
try {
    $os=Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    Add-Line ('检查时间：'+(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
    $name=if([int]$os.CurrentBuildNumber -ge 22000){'Windows 11（注册名称：'+$os.ProductName+'）'}else{$os.ProductName}
    Add-Line ('系统：'+$name+'；版本：'+$os.DisplayVersion+'；内部版本：'+$os.CurrentBuildNumber+'.'+$os.UBR)
    Add-Line ('操作系统位数：'+$(if([Environment]::Is64BitOperatingSystem){'64位'}else{'32位'}))
    $locale=if(Get-Command Get-WinSystemLocale -ErrorAction SilentlyContinue){Get-WinSystemLocale}else{Get-Culture}
    Add-Line ('系统区域：'+$locale.Name+'；控制台代码页：'+[Console]::OutputEncoding.CodePage)
    foreach($relative in @('a12_server.exe','login_service/a12_registration_server.exe','login_service_x64_compat/a12_registration_server.exe','../ACT12/DNF.exe')){
        $file=Join-Path $PSScriptRoot $relative
        if(Test-Path -LiteralPath $file -PathType Leaf){Add-Line ($relative+'：'+(Image-Machine $file))}else{Add-Line ($relative+'：文件不存在')}
    }
    $root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
    $python=Join-Path $root 'runtime\python\python.exe'
    $helper=Join-Path $root 'trainer\login_service.py'
    if((Test-Path -LiteralPath $python -PathType Leaf) -and (Test-Path -LiteralPath $helper -PathType Leaf)){
        $report=& $python -I -B $helper runtime | ConvertFrom-Json
        if($report.ok){
            $runtime=$report.runtime
            Add-Line ('登录服务运行：成功；入口='+$report.backend+'；进程='+$runtime.Bitness+'位；SQLite='+$runtime.SqliteVersion+'；TLS='+$runtime.TlsProtocols)
            foreach($attempt in $report.previousAttempts){Add-Line ('运行库回退：'+$attempt.backend+' '+$attempt.code)}
        }else{Add-Line ('登录服务运行失败：['+$report.code+'] '+$report.message)}
    }else{Add-Line '缺少随包的兼容检查组件，请完整解压游戏包。'}
    if([int]$os.CurrentBuildNumber -lt 14393 -and !(Test-Path -LiteralPath (Join-Path $root 'runtime\python\python38._pth'))){
        Add-Line '标准包要求 Windows 10 1607 或更新版本。Win7 SP1 请使用专用兼容包。'
    }
    foreach($name in @('msyh.ttc','simhei.ttf','simsun.ttc')){Add-Line ('中文字体'+$name+'：'+(Test-Path -LiteralPath (Join-Path $env:WINDIR ('Fonts/'+$name))))}
    Add-Line '此检查未修改系统配置、证书或游戏数据库。'
}catch {Add-Line ('检查错误：'+$_.Exception.Message)}
[IO.File]::WriteAllLines($ReportFile,$lines,(New-Object System.Text.UTF8Encoding($true)))
Write-Host ('结果已保存：'+$ReportFile)
