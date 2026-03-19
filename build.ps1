# CloudflareST multi-platform build script
# Usage: .\build.ps1 [platform] [mode] [-upx] [-clean] [-unity]

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
# -unity  构建 Unity 兼容 DLL（netstandard2.1 目标）
#         输出: publish/cfst.dll + publish/Cronos.dll
#         将这两个文件放入 Unity 项目的 Assets/Plugins/ 目录
#         可与 -clean 组合使用，不可与 -fd/-aot/-upx 组合
#         示例: .\build.ps1 -unity
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
    [ValidateSet('all', 'win-x64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$Platform = 'all',
    [switch]$fd,
    [switch]$aot,
    [switch]$upx,
    [switch]$unity,
    [switch]$clean
)

$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'
$ProjectRoot = $PSScriptRoot
$PublishBase = Join-Path $ProjectRoot 'publish'

$Targets = @{
    'win-x64'    = @{ Rid = 'win-x64';    Exe = 'cfst.exe'; Desc = 'Windows x64' }
    'linux-x64'  = @{ Rid = 'linux-x64';  Exe = 'cfst';     Desc = 'Linux x64' }
    'linux-arm64' = @{ Rid = 'linux-arm64'; Exe = 'cfst';   Desc = 'Linux ARM64 (Raspberry Pi 4/5, ARM servers)' }
    'osx-x64'   = @{ Rid = 'osx-x64';   Exe = 'cfst';     Desc = 'macOS Intel (x64)' }
    'osx-arm64' = @{ Rid = 'osx-arm64'; Exe = 'cfst';     Desc = 'macOS Apple Silicon (M1/M2/M3)' }
}

function Write-BuildOutput {
    param([string[]]$output)
    $hasError = $false
    foreach ($line in $output) {
        # 跳过已过滤的 SDK 警告和纯 warning 行
        if ($line -match 'NETSDK1124|NETSDK1207') { continue }
        if ($line -match ':\s*warning\s') { continue }
        if ($line -match '^\s*warning\s') { continue }
        # error 行红色显示
        if ($line -match ':\s*error\s|^\s*error\s|生成失败|Build FAILED') {
            Write-Host $line -ForegroundColor Red
            $hasError = $true
        } else {
            Write-Host $line
        }
    }
    return $hasError
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
        $output = dotnet publish -r $Rid -f net8.0 -c Release `
            -p:NoWarn=NETSDK1124 `
            -p:PublishAot=true `
            -p:StripSymbols=true `
            -o $OutDir `
            $ProjectRoot 2>&1
    } elseif ($FrameworkDependent) {
        $output = dotnet publish -r $Rid -f net8.0 -c Release --self-contained false `
            -p:NoWarn=NETSDK1124 `
            -p:PublishSingleFile=true `
            -p:PublishTrimmed=false `
            -p:EnableCompressionInSingleFile=false `
            -o $OutDir `
            $ProjectRoot 2>&1
    } else {
        $output = dotnet publish -r $Rid -f net8.0 -c Release --self-contained true `
            -p:NoWarn=NETSDK1124 `
            -p:PublishSingleFile=true `
            -p:PublishTrimmed=true `
            -p:EnableCompressionInSingleFile=true `
            -o $OutDir `
            $ProjectRoot 2>&1
    }

    $hasError = Write-BuildOutput -output $output
    if ($hasError) { return }

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
        # only -unity: skip CLI publish
        $cliSkip = $unity.IsPresent -and -not ($fd.IsPresent -or $aot.IsPresent -or $upx.IsPresent) -and -not $PSBoundParameters.ContainsKey('Platform')
        if (-not $cliSkip) {
            $mode = 'self-contained'
            if ($fd)  { $mode = 'fd' }
            if ($aot) { $mode = 'NativeAOT' }
            $upxNote = if ($upx) { ' +UPX' } else { '' }
            Write-Host "CloudflareST build - Project: $ProjectRoot [$mode$upxNote]" -ForegroundColor Yellow
            if ($Platform -eq 'all') {
                foreach ($key in @('win-x64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')) {
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
    }

    # Unity DLL 构建（-unity 参数触发）
    if ($unity) {
        $UnityOutDir = Join-Path $PublishBase 'unity-netstandard2.1'
        Write-Host "`n==> Unity DLL (netstandard2.1)" -ForegroundColor Cyan
        $buildOutput = dotnet build -f netstandard2.1 -c Release `
            -p:CopyLocalLockFileAssemblies=true `
            -o $UnityOutDir `
            $ProjectRoot 2>&1
        $unityHasError = Write-BuildOutput -output $buildOutput
        if ($unityHasError) {
            Write-Host "    [错误] Unity DLL 构建失败，已中止。" -ForegroundColor Red
            return
        }
        # 收集 Unity 所需文件：主 DLL + Cronos.dll
        $dllFiles = @(
            (Join-Path $UnityOutDir 'cfst.dll'),
            (Join-Path $UnityOutDir 'Cronos.dll')
        )
        foreach ($dll in $dllFiles) {
            if (Test-Path $dll) {
                $name = Split-Path $dll -Leaf
                Copy-Item $dll (Join-Path $PublishBase $name) -Force
                $size = [math]::Round((Get-Item $dll).Length / 1KB, 1)
                Write-Host "    Output: $PublishBase\$name ($size KB)" -ForegroundColor Green
            }
        }
        Write-Host "`n[Unity 使用说明]" -ForegroundColor Yellow
        Write-Host "  将以下文件复制到 Unity 项目的 Assets/Plugins/ 目录：" -ForegroundColor Yellow
        Write-Host "    cfst.dll      -- 本项目主库" -ForegroundColor Yellow
        Write-Host "    Cronos.dll    -- 定时调度依赖（如不使用 -cron 参数可跳过）" -ForegroundColor Yellow
    }
} finally {
    Pop-Location
}
