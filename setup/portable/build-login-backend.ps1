param(
    [Parameter(Mandatory=$true)][string]$SourceDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateSet('net9.0','net6.0')][string]$Framework = 'net9.0'
)
$ErrorActionPreference='Stop'
# Build the matching external registration-service source in a scratch directory.
# Only the management listener and its existing Host check gain a port setting.
$source=(Resolve-Path -LiteralPath $SourceDirectory).Path
$output=[IO.Path]::GetFullPath($OutputDirectory)
$scratch=New-Item -ItemType Directory -Path (Join-Path ([IO.Path]::GetTempPath()) ('arad-login-'+[Guid]::NewGuid().ToString('N')))
function Replace-One([string]$path,[string]$old,[string]$new) {
    $content=[IO.File]::ReadAllText($path)
    if(([regex]::Matches($content,[regex]::Escape($old))).Count -ne 1){throw "Unexpected login service source in $path"}
    [IO.File]::WriteAllText($path,$content.Replace($old,$new))
}
try {
    Get-ChildItem -LiteralPath $source -File | Copy-Item -Destination $scratch.FullName
    Copy-Item -LiteralPath (Join-Path $source 'Properties') -Destination $scratch.FullName -Recurse
    $project=Join-Path $scratch.FullName 'a12_registration_server.csproj'
    [xml]$spec=Get-Content -LiteralPath $project -Raw
    $spec.SelectSingleNode('//TargetFramework').InnerText=$Framework
    $spec.Save($project)
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LoginManagementPort.cs') -Destination $scratch.FullName
    $web=Join-Path $scratch.FullName 'GmWebServer.cs'
    Replace-One $web 'ListenAnyIP(8080)' 'ListenAnyIP(LoginManagementPort.Value)'
    Replace-One $web 'Console.WriteLine("网页 GM：本机各 IP 的 8080 端口（仅本机来源可访问）")' 'Console.WriteLine("Local management port: " + LoginManagementPort.Value)'
    Replace-One (Join-Path $scratch.FullName 'GmWebAccessPolicy.cs') 'result.Port != 8080' 'result.Port != LoginManagementPort.Value'
    Replace-One (Join-Path $scratch.FullName 'Program.cs') 'Architecture = RuntimeInformation.ProcessArchitecture.ToString(),' 'ConfigurableManagementPort = true, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),'
    foreach($arch in @('x64','x86')){
        $dest=Join-Path $output $arch
        dotnet publish $project -c Release -r "win-$arch" --source 'https://api.nuget.org/v3/index.json' --self-contained true "-p:PlatformTarget=$arch" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $dest --nologo
        if($LASTEXITCODE -ne 0){throw "Login backend build failed: $Framework $arch"}
    }
} finally {
    $temporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    $resolved=(Resolve-Path -LiteralPath $scratch.FullName).Path
    if($resolved.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $resolved) -match '^arad-login-[0-9a-f]{32}$'){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
