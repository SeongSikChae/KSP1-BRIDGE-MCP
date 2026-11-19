#Requires -Version 7
<#
.SYNOPSIS
    빌드하고 KSP GameData/KspBridge와 서버 폴더(ServerDir)에 배포한다.

.DESCRIPTION
    - 경로는 KspBridge.user.props의 KspDir, ServerDir에서 읽는다. ServerDir는 비워도 된다(서버 호스트 배포를 건너뛴다).
    - 서버 모듈은 ServerDir/modules와, 실행 중인 서버가 %LOCALAPPDATA%\KspBridge\servers에 등록한 모듈 폴더에 모두 배포한다.
      그래서 VS 게시 등으로 다른 폴더에서 띄운 서버도 경로 설정 없이 모듈이 갱신된다. 죽은 서버의 등록 파일은 지운다.
    - Mod 로직 어셈블리는 배포마다 새 버전(ModBuildStamp)으로 빌드한다. Mono는 바이트로 읽은 어셈블리를 이름으로 묶기 때문이다.
    - 고정 호스트(Mod Loader DLL, 서버 exe와 호스트 DLL)는 내용이 바뀐 파일만 복사한다. 실행 중이라 잠겨 있으면 재시작 안내를 출력하고 계속한다.
    - 모듈 DLL을 다 복사한 뒤 manifest.json을 마지막에 쓴다. 실행 중인 KSP와 서버는 이 파일 변경을 보고 새 세대를 읽는다.
    - 서버를 띄우거나 끄지 않는다(서버는 사용자가 따로 실행한다).

.EXAMPLE
    ./scripts/deploy.ps1
    ./scripts/deploy.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)
# 빌드를 건너뛰는 옵션은 두지 않는다. Mod 로직은 배포마다 새 이름으로 빌드해야 KSP가 새 세대로 읽는다.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'KspBridge.slnx'
$restartNotes = [System.Collections.Generic.List[string]]::new()

function Get-MsBuildProperty([string]$project, [string]$name) {
    $value = (& dotnet msbuild $project -getProperty:$name -nologo).Trim()
    if ($LASTEXITCODE -ne 0) { throw "msbuild -getProperty:$name failed for $project" }
    return $value
}

function Get-OutputDir([string]$projectName, [string]$tfm) {
    return Join-Path $root "src/$projectName/bin/$Configuration/$tfm"
}

function Test-SameFile([string]$a, [string]$b) {
    if (-not (Test-Path $b)) { return $false }
    return (Get-FileHash $a).Hash -eq (Get-FileHash $b).Hash
}

# 고정 호스트 파일: 바뀐 파일만 복사한다. 바뀐 파일 수를 돌려주고, 잠겨 있으면 재시작 안내를 남긴다.
function Copy-HostFile([string]$source, [string]$destination, [string]$hostName) {
    if (Test-SameFile $source $destination) { return 0 }
    New-Item -ItemType Directory -Force (Split-Path -Parent $destination) | Out-Null
    try {
        Copy-Item $source $destination -Force
        return 1
    }
    catch [System.IO.IOException], [System.UnauthorizedAccessException] {
        $restartNotes.Add("$hostName 실행 중이라 $([System.IO.Path]::GetFileName($destination)) 파일을 복사하지 못했다. 종료한 뒤 다시 배포한다.")
        return 0
    }
}

# 실행 중인 고정 호스트의 파일이 바뀌었으면 재시작 안내를 남긴다(바뀐 호스트는 재시작해야 반영된다).
function Add-RestartNote([int]$changed, [string]$processName, [string]$hostName) {
    if ($changed -gt 0 -and (Get-Process $processName -ErrorAction SilentlyContinue)) {
        $restartNotes.Add("$hostName 호스트 파일 $changed 개가 바뀌었다. 다시 시작해야 반영된다.")
    }
}

# 모듈 DLL을 복사하고 manifest.json을 마지막에 원자적으로 쓴다.
function Publish-Modules([string]$modulesDir, [string[]]$moduleFiles, [string]$build) {
    New-Item -ItemType Directory -Force $modulesDir | Out-Null
    foreach ($file in $moduleFiles) {
        Copy-Item $file (Join-Path $modulesDir ([System.IO.Path]::GetFileName($file))) -Force
        $pdb = [System.IO.Path]::ChangeExtension($file, '.pdb')
        if (Test-Path $pdb) { Copy-Item $pdb (Join-Path $modulesDir ([System.IO.Path]::GetFileName($pdb))) -Force }
    }
    $manifest = [ordered]@{ build = $build; modules = @($moduleFiles | ForEach-Object { [System.IO.Path]::GetFileName($_) }) }
    $temp = Join-Path $modulesDir 'manifest.json.tmp'
    $manifest | ConvertTo-Json | Set-Content -Path $temp -Encoding utf8NoBOM
    Move-Item $temp (Join-Path $modulesDir 'manifest.json') -Force
    Write-Host "  manifest: $(Join-Path $modulesDir 'manifest.json') ($($manifest.modules -join ', '))"

    # 매니페스트에 없는 이전 세대 파일을 지운다. 실행 중인 호스트는 모듈을 바이트/스트림으로 읽어 파일을 잡고 있지 않다.
    $keep = @($manifest.modules | ForEach-Object { $_; [System.IO.Path]::ChangeExtension($_, '.pdb') })
    Get-ChildItem -Path (Join-Path $modulesDir '*') -File -Include '*.dll', '*.pdb' |
        Where-Object { $keep -notcontains $_.Name } |
        ForEach-Object { Remove-Item $_.FullName -Force; Write-Host "  이전 세대 삭제: $($_.Name)" }
}

# 실행 중인 서버가 등록한 모듈 폴더(KspBridge.Server의 ServerRegistration). pid와 프로세스 시작 시각이 맞는 등록만 쓰고, 나머지는 지운다.
function Get-RunningServerModulesDirs {
    $registry = Join-Path $env:LOCALAPPDATA 'KspBridge/servers'
    if (-not (Test-Path $registry)) { return @() }
    $dirs = @()
    foreach ($file in Get-ChildItem $registry -Filter '*.json' -File) {
        $alive = $false
        try {
            $entry = Get-Content $file.FullName -Raw | ConvertFrom-Json
            $registered = if ($entry.startTime -is [datetime]) { $entry.startTime.ToUniversalTime() } else { [datetimeoffset]::Parse($entry.startTime).UtcDateTime }
            $process = Get-Process -Id $entry.pid -ErrorAction SilentlyContinue
            $alive = $process -and [Math]::Abs(($process.StartTime.ToUniversalTime() - $registered).TotalSeconds) -lt 1
        }
        catch { $alive = $false }
        if ($alive) {
            Write-Host "  실행 중 서버 발견: pid $($entry.pid), port $($entry.port), $($entry.modulesDir)"
            $dirs += $entry.modulesDir
        }
        else {
            Remove-Item $file.FullName -Force -ErrorAction SilentlyContinue
            Write-Host "  끝난 서버 등록 삭제: $($file.Name)"
        }
    }
    return $dirs
}

# Mod 로직 어셈블리 파일. 배포 빌드는 이름에 세대 표식(.g<stamp>)이 붙는다(Unity Mono가 같은 단순 이름의 이전 세대를 돌려주므로).
function Get-ModAssembly([string]$projectName) {
    $file = Join-Path (Get-OutputDir $projectName 'net472') "$projectName.g$($stamp.Replace('.', '_')).dll"
    if (-not (Test-Path $file)) { throw "빌드 산출물이 없다: $file" }
    return $file
}

$loaderProject = Join-Path $root 'src/KspBridge.Mod.Loader/KspBridge.Mod.Loader.csproj'
$kspDir = Get-MsBuildProperty $loaderProject 'KspDir'
$serverDir = Get-MsBuildProperty (Join-Path $root 'src/KspBridge.Server/KspBridge.Server.csproj') 'ServerDir'
if (-not $kspDir -or -not (Test-Path (Join-Path $kspDir 'KSP_x64.exe'))) { throw "KspDir가 KSP 설치 폴더가 아니다: '$kspDir' (KspBridge.user.props 확인)" }

# 배포마다 다른 Mod 로직 버전: 0.1.<2026-01-01부터 일수>.<하루 중 2초 단위>
$now = (Get-Date).ToUniversalTime()
$stamp = '{0}.{1}' -f ([int][Math]::Floor(($now - [datetime]'2026-01-01').TotalDays) % 65535), ([int][Math]::Floor($now.TimeOfDay.TotalSeconds / 2))

Write-Host "빌드 ($Configuration, ModBuildStamp=$stamp)"
& dotnet build $solution -c $Configuration -nologo -v:minimal "-p:ModBuildStamp=$stamp"
if ($LASTEXITCODE -ne 0) { throw 'dotnet build 실패' }

# ---- KSP Mod ----
$gameData = Join-Path $kspDir 'GameData/KspBridge'
Write-Host "Mod 배포: $gameData"
$loaderOut = Get-OutputDir 'KspBridge.Mod.Loader' 'net472'
$loaderChanged = Copy-HostFile (Join-Path $loaderOut 'KspBridge.Mod.Loader.dll') (Join-Path $gameData 'Plugins/KspBridge.Mod.Loader.dll') 'KSP'
Write-Host "  Loader: $(if ($loaderChanged) { '갱신' } else { '변경 없음' })"
Add-RestartNote $loaderChanged 'KSP_x64' 'KSP'
$settings = Join-Path $gameData 'settings.cfg'
if (-not (Test-Path $settings)) { Copy-Item (Join-Path $loaderOut 'settings.cfg') $settings }

$modModules = @(Get-ModAssembly 'KspBridge.Mod.Core')
$modModules += Get-ChildItem (Join-Path $root 'src') -Directory -Filter 'KspBridge.Mod.Modules.*' | Sort-Object Name |
    ForEach-Object { Get-ModAssembly $_.Name }
Publish-Modules (Join-Path $gameData 'PluginData/modules') $modModules $stamp

# ---- MCP 서버 ----
$serverModulesDirs = @()
if ($serverDir) {
    Write-Host "서버 배포: $serverDir"
    $serverOut = Get-OutputDir 'KspBridge.Server' 'net10.0'
    $serverChanged = 0
    foreach ($file in Get-ChildItem $serverOut -File -Recurse) {
        $relative = [System.IO.Path]::GetRelativePath($serverOut, $file.FullName)
        if ($relative -like 'appsettings.Development.json') { continue }
        $serverChanged += Copy-HostFile $file.FullName (Join-Path $serverDir $relative) '서버'
    }
    Write-Host "  서버 호스트: $(if ($serverChanged) { "$serverChanged 개 파일 갱신" } else { '변경 없음' })"
    Add-RestartNote $serverChanged 'KspBridge.Server' '서버'
    $config = Join-Path $serverDir 'kspbridge.yaml'
    if (-not (Test-Path $config)) {
        @"
ContentRootPath: $($serverDir -replace '\\', '/')
Port: 65531
ModUrl: http://127.0.0.1:65530
RequestTimeoutMs: 3000
ModulesDir: modules
"@ | Set-Content -Path $config -Encoding utf8NoBOM
        Write-Host "  설정 생성: $config"
    }
    $serverModulesDirs += Join-Path $serverDir 'modules'
}
else {
    Write-Host '서버 호스트 배포 건너뜀 (ServerDir 비어 있음)'
}

Write-Host '서버 모듈 배포 대상'
$serverModulesDirs += Get-RunningServerModulesDirs
$serverModulesDirs = @($serverModulesDirs | ForEach-Object { [System.IO.Path]::GetFullPath($_).TrimEnd('\', '/') } |
    Sort-Object -Unique)
if ($serverModulesDirs.Count -eq 0) {
    Write-Host '  대상 없음 (ServerDir가 비어 있고 실행 중인 서버도 없다)' -ForegroundColor Yellow
}

$serverModules = Get-ChildItem (Join-Path $root 'src') -Directory -Filter 'KspBridge.Server.Modules.*' |
    Sort-Object @{ Expression = { $_.Name -ne 'KspBridge.Server.Modules.Core' } }, Name |
    ForEach-Object { Join-Path (Get-OutputDir $_.Name 'net10.0') "$($_.Name).dll" }
foreach ($modulesDir in $serverModulesDirs) {
    Publish-Modules $modulesDir $serverModules $stamp
}

Write-Host ''
if ($restartNotes.Count -gt 0) {
    Write-Host '재시작 안내:' -ForegroundColor Yellow
    $restartNotes | Sort-Object -Unique | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
}
Write-Host "배포 완료 (build $stamp)."
if ($serverDir) { Write-Host "서버 실행: `"$serverDir\KspBridge.Server.exe`" --config `"$config`" --log `"$serverDir\logs`"" }
