# CloudflareST multi-platform build script
# Usage: .\build.ps1 [platform] [mode] [-clean]
#
# === 构建模式 ===
# (默认)          自包含单文件 + Trim + 压缩，约 15~30 MB，无需安装 .NET
#                 dotnet publish --self-contained true -p:PublishSingleFile=true
#                   -p:PublishTrimmed=true -p:EnableCompressionInSingleFile=true
#
# -fd             框架依赖单文件，约几百 KB，目标机器需安装 .NET 8 Runtime
#                 dotnet publish --self-contained false -p:PublishSingleFile=true
#
# -aot            NativeAOT：编译为原生二进制，无需 .NET 运行时，启动最快，体积最小
#                 dotnet publish -p:PublishAot=true
#                 前置依赖：
#                   Windows : Visual Studio 2022 + "使用 C++ 的桌面开发" 工作负载
#                   Linux   : clang, zlib1g-dev  (sudo apt install clang zlib1g-dev)
#                   macOS   : Xcode Command Line Tools (xcode-select --install)
#                 注意：AOT 不支持部分反射/动态代码，构建时会有警告提示不兼容项
#
# === 清理 ===
# -clean          清理 obj/bin/publish 目录（可与平台/模式组合使用）
#
# === 示例 ===
# .\build.ps1                        # 全平台自包含
# .\build.ps1 win-x64                # 仅 Windows x64 自包含
# .\build.ps1 -fd                    # 全平台框架依赖
# .\build.ps1 win-x64 -fd            # 仅 Windows x64 框架依赖
# .\build.ps1 win-x64 -aot           # 仅 Windows x64 NativeAOT
# .\build.ps1 -aot                   # 全平台 NativeAOT（需各平台工具链）
# .\build.ps1 -clean                 # 仅清理
# .\build.ps1 win-x64 -clean         # 清理后构建 win-x64

param(
    [Parameter(Position = 0)]
    [ValidateSet('all', 'win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')]
    [string]$Platform = 'all',
    [switch]$fd,
    [switch]$aot,
    [switch]$clean
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$PublishBase = Join-Path $ProjectRoot 'publish'

$Targets = @{
    'win-x64'   = @{ Rid = 'win-x64';   Exe = 'cfst.exe'; Desc = 'Windows x64' }
    'linux-x64' = @{ Rid = 'linux-x64'; Exe = 'cfst';     Desc = 'Linux x64' }
    'osx-x64'   = @{ Rid = 'osx-x64';   Exe = 'cfst';     Desc = 'macOS Intel (x64)' }
    'osx-arm64' = @{ Rid = 'osx-arm64'; Exe = 'cfst';     Desc = 'macOS Apple Silicon (M1/M2/M3)' }
}

function Publish-Rid {
    param([string]$Rid, [string]$Desc, [string]$Exe, [bool]$FrameworkDependent, [bool]$NativeAot)
    $suffix = ''
    if ($FrameworkDependent) { $suffix = '-fd' }
    if ($NativeAot)          { $suffix = '-aot' }
    $OutDir = Join-Path $PublishBase "$Rid$suffix"
    $mode = 'self-contained'
    if ($FrameworkDependent) { $mode = 'fd (需 .NET 8 Runtime)' }
    if ($NativeAot)          { $mode = 'NativeAOT' }
    Write-Host "`n==> $Desc ($Rid) [$mode]" -ForegroundColor Cyan

    if ($NativeAot) {
        dotnet publish -r $Rid -c Release `
            -p:PublishAot=true `
            -p:StripSymbols=true `
            -o $OutDir `
            $ProjectRoot
    } elseif ($FrameworkDependent) {
        dotnet publish -r $Rid -c Release --self-contained false `
            -p:PublishSingleFile=true `
            -p:PublishTrimmed=false `
            -p:EnableCompressionInSingleFile=false `
            -o $OutDir `
            $ProjectRoot
    } else {
        dotnet publish -r $Rid -c Release --self-contained true `
            -p:PublishSingleFile=true `
            -p:PublishTrimmed=true `
            -p:EnableCompressionInSingleFile=true `
            -o $OutDir `
            $ProjectRoot
    }

    $ExePath = Join-Path $OutDir $Exe
    if (Test-Path $ExePath) {
        $finalName = if ($Rid -eq 'win-x64') { "cfst-$Rid$suffix.exe" } else { "cfst-$Rid$suffix" }
        $FinalPath = Join-Path $PublishBase $finalName
        Copy-Item $ExePath $FinalPath -Force
        $Size = (Get-Item $FinalPath).Length / 1MB
        Write-Host "    Output: $FinalPath ($([math]::Round($Size, 2)) MB)" -ForegroundColor Green
    }
}

Push-Location $ProjectRoot
try {
    $skipBuild = $false
    if ($clean) {
        Write-Host "Cleaning obj/bin/publish..." -ForegroundColor Yellow
        Remove-Item -Recurse -Force (Join-Path $ProjectRoot 'obj') -ErrorAction SilentlyContinue
        Remove-Item -Recurse -Force (Join-Path $ProjectRoot 'bin') -ErrorAction SilentlyContinue
        Remove-Item -Recurse -Force $PublishBase -ErrorAction SilentlyContinue
        Write-Host "Clean done." -ForegroundColor Green
        if (-not $PSBoundParameters.ContainsKey('Platform')) {
            $skipBuild = $true
        }
    }

    if (-not $skipBuild) {
        $mode = 'self-contained'
        if ($fd)  { $mode = 'fd' }
        if ($aot) { $mode = 'NativeAOT' }
        Write-Host "CloudflareST build - Project: $ProjectRoot [$mode]" -ForegroundColor Yellow
        if ($Platform -eq 'all') {
            foreach ($key in @('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')) {
                $t = $Targets[$key]
                Publish-Rid -Rid $t.Rid -Desc $t.Desc -Exe $t.Exe -FrameworkDependent $fd.IsPresent -NativeAot $aot.IsPresent
            }
        } else {
            $t = $Targets[$Platform]
            Publish-Rid -Rid $t.Rid -Desc $t.Desc -Exe $t.Exe -FrameworkDependent $fd.IsPresent -NativeAot $aot.IsPresent
        }
        Write-Host "`nDone. Output: $PublishBase" -ForegroundColor Green
    }
} finally {
    Pop-Location
}
