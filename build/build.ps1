# CloudflareST multi-platform build script
# Usage: .\build.ps1 [platform] [mode] [-upx] [-clean]
#
# === 构建模式 ===
#
# (默认)  自包含单文件
#         --self-contained true  -p:PublishSingleFile=true
#         -p:PublishTrimmed=true -p:EnableCompressionInSingleFile=true
#         裁剪(Trim): YES  内置压缩: YES  需要运行时: NO   体积: ~25 MB
#
# -fd     框架依赖单文件
#         --self-contained false -p:PublishSingleFile=true
#         裁剪(Trim): NO   内置压缩: NO   需要运行时: YES(.NET 8)  体积: ~几百 KB
#
# -aot    NativeAOT 原生编译
#         -p:PublishAot=true -p:StripSymbols=true
#         裁剪(Trim): YES(强制) 内置压缩: NO  需要运行时: NO  体积: ~10 MB
#         前置依赖:
#           Windows : Visual Studio 2022 + "使用 C++ 的桌面开发" 工作负载
#           Linux   : clang, zlib1g-dev  (sudo apt install clang zlib1g-dev)
#           macOS   : Xcode Command Line Tools (xcode-select --install)
#         注意: AOT 不支持部分反射/动态代码，构建时会有警告提示不兼容项
#
# === UPX 压缩（可叠加到任意模式）===
#
# -upx    使用 UPX 对输出文件再次压缩（需已安装 UPX 并在 PATH 中）
#         效果: 在内置压缩基础上再缩小约 30~50%
#         默认(自包含)+upx : ~25 MB -> ~12 MB
#         aot+upx          : ~10 MB -> ~5 MB
#         fd+upx           : 效果有限，不推荐
#         注意: UPX 压缩的文件部分杀软可能误报，面向用户发布时请酌情使用
#
#         注意: macOS 的 NativeAOT 输出为 Mach-O 格式，UPX 不支持压缩，macOS 下 -aot -upx 组合无效。
#
#         UPX 安装方式:
#           Windows : scoop install upx
#                     或 choco install upx
#                     或 winget install upx
#                     或下载 https://github.com/upx/upx/releases 解压到 PATH
#           macOS   : brew install upx  (仅自包含/fd 模式有效，NativeAOT 无效)
#           Linux   : sudo apt install upx        # Debian/Ubuntu
#                     sudo yum install upx        # CentOS/RHEL
#                     sudo pacman -S upx          # Arch
#                     sudo apk add upx            # Alpine
#
# === 清理 ===
#
# -clean  清理 obj/bin/publish 目录（可与其他参数组合）
#
# === 示例 ===
# .\build.ps1                        # 全平台 自包含+Trim+内置压缩  ~25 MB
# .\build.ps1 win-x64                # 仅 win-x64 自包含
# .\build.ps1 -fd                    # 全平台 框架依赖  ~几百 KB
# .\build.ps1 win-x64 -fd            # 仅 win-x64 框架依赖
# .\build.ps1 -aot                   # 全平台 NativeAOT  ~10 MB
# .\build.ps1 win-x64 -aot           # 仅 win-x64 NativeAOT
# .\build.ps1 -upx                   # 全平台 自包含 + UPX 二次压缩  ~12 MB
# .\build.ps1 win-x64 -aot -upx      # win-x64 NativeAOT + UPX  ~5 MB（最小）
# .\build.ps1 -clean                 # 仅清理
# .\build.ps1 win-x64 -clean         # 清理后构建 win-x64

param(
    [Parameter(Position = 0)]
    [ValidateSet('all', 'win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')]
    [string]$Platform = 'all',
    [switch]$fd,
    [switch]$aot,
    [switch]$upx,
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
    param([string]$Rid, [string]$Desc, [string]$Exe, [bool]$FrameworkDependent, [bool]$NativeAot, [bool]$UseUpx)
    $suffix = ''
    if ($FrameworkDependent) { $suffix = '-fd' }
    if ($NativeAot)          { $suffix = '-aot' }
    $OutDir = Join-Path $PublishBase "$Rid$suffix"
    $mode = 'self-contained'
    if ($FrameworkDependent) { $mode = 'fd' }
    if ($NativeAot)          { $mode = 'NativeAOT' }
    Write-Host "`n==> $Desc ($Rid) [$mode$(if ($UseUpx) { '+UPX' })]" -ForegroundColor Cyan

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
    if (-not (Test-Path $ExePath)) { return }

    # UPX 压缩
    if ($UseUpx) {
        $upxCmd = Get-Command upx -ErrorAction SilentlyContinue
        if ($upxCmd) {
            Write-Host "    UPX compressing..." -ForegroundColor DarkCyan
            & upx --best --lzma $ExePath | Out-Null
        } else {
            Write-Host "    [警告] 未找到 upx，跳过 UPX 压缩。请安装: scoop install upx" -ForegroundColor Yellow
        }
    }

    $finalName = if ($Rid -eq 'win-x64') { "cfst-$Rid$suffix.exe" } else { "cfst-$Rid$suffix" }
    $FinalPath = Join-Path $PublishBase $finalName
    Copy-Item $ExePath $FinalPath -Force
    $Size = (Get-Item $FinalPath).Length / 1MB
    Write-Host "    Output: $FinalPath ($([math]::Round($Size, 2)) MB)" -ForegroundColor Green
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
        $upxNote = if ($upx) { ' +UPX' } else { '' }
        Write-Host "CloudflareST build - Project: $ProjectRoot [$mode$upxNote]" -ForegroundColor Yellow
        if ($Platform -eq 'all') {
            foreach ($key in @('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')) {
                $t = $Targets[$key]
                Publish-Rid -Rid $t.Rid -Desc $t.Desc -Exe $t.Exe `
                    -FrameworkDependent $fd.IsPresent `
                    -NativeAot $aot.IsPresent `
                    -UseUpx $upx.IsPresent
            }
        } else {
            $t = $Targets[$Platform]
            Publish-Rid -Rid $t.Rid -Desc $t.Desc -Exe $t.Exe `
                -FrameworkDependent $fd.IsPresent `
                -NativeAot $aot.IsPresent `
                -UseUpx $upx.IsPresent
        }
        Write-Host "`nDone. Output: $PublishBase" -ForegroundColor Green
    }
} finally {
    Pop-Location
}
