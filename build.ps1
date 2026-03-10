# CloudflareST multi-platform build script
# Usage: .\build.ps1 [platform] [-fd] [-unity]
#        .\build.ps1 -clean    # clean obj, bin, publish
# Example: .\build.ps1
#          .\build.ps1 win-x64
#          .\build.ps1 -fd
#          .\build.ps1 -unity   # build Core DLL for Unity and copy to UnityPackage

param(
    [Parameter(Position = 0)]
    [ValidateSet('all', 'win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')]
    [string]$Platform = 'all',
    [switch]$fd,
    [switch]$clean,
    [switch]$unity
)

$ErrorActionPreference = 'Stop'
$ProjectRoot  = $PSScriptRoot
$PublishBase  = Join-Path $ProjectRoot 'publish'
$CliProject   = Join-Path $ProjectRoot 'CloudflareST.Cli\CloudflareST.Cli.csproj'
$CoreProject  = Join-Path $ProjectRoot 'CloudflareST.Core\CloudflareST.Core.csproj'
$UnityPkgPath = Join-Path $ProjectRoot 'UnityPackage'

# ── Clean ────────────────────────────────────────────────────────────────────
if ($clean) {
    $dirs = @('obj', 'bin', 'publish')
    foreach ($d in $dirs) {
        $path = Join-Path $ProjectRoot $d
        if (Test-Path $path) {
            Remove-Item -Path $path -Recurse -Force
            Write-Host "Cleaned: $path" -ForegroundColor Yellow
        }
    }
    $subDirs = @('CloudflareST.Core', 'CloudflareST.Cli', 'CloudflareST.Tests')
    foreach ($sub in $subDirs) {
        foreach ($d in $dirs) {
            $path = Join-Path $ProjectRoot "$sub\$d"
            if (Test-Path $path) {
                Remove-Item -Path $path -Recurse -Force
                Write-Host "Cleaned: $path" -ForegroundColor Yellow
            }
        }
    }
    Write-Host 'Clean done.' -ForegroundColor Green
    exit 0
}

# ── Unity DLL Build ──────────────────────────────────────────────────────────
function Build-UnityDll {
    Write-Host ''
    Write-Host '==> Building CloudflareST.Core for Unity (netstandard2.0)...' -ForegroundColor Cyan

    dotnet build "$CoreProject" -c Release -f netstandard2.0

    $coreDir  = Split-Path $CoreProject -Parent
    $dllPath  = Join-Path $coreDir 'bin\Release\netstandard2.0\CloudflareST.Core.dll'

    if (-not (Test-Path $dllPath)) {
        Write-Warning "CloudflareST.Core.dll not found at: $dllPath"
        return
    }

    # Also look for System.Text.Json DLL needed at runtime for netstandard2.0
    $depsDir = Join-Path $coreDir 'bin\Release\netstandard2.0'

    if (-not (Test-Path $UnityPkgPath)) {
        New-Item -ItemType Directory -Force -Path $UnityPkgPath | Out-Null
    }

    Copy-Item $dllPath -Destination (Join-Path $UnityPkgPath 'CloudflareST.Core.dll') -Force
    Write-Host "    Copied: CloudflareST.Core.dll  ->  $UnityPkgPath" -ForegroundColor Green

    # Copy System.Text.Json and Newtonsoft deps if present
    $depDlls = @('System.Text.Json.dll', 'System.Text.Encodings.Web.dll', 'System.Memory.dll', 'System.Buffers.dll', 'System.Runtime.CompilerServices.Unsafe.dll')
    foreach ($dep in $depDlls) {
        $depSrc = Join-Path $depsDir $dep
        if (Test-Path $depSrc) {
            Copy-Item $depSrc -Destination (Join-Path $UnityPkgPath $dep) -Force
            Write-Host "    Copied dep: $dep" -ForegroundColor DarkGreen
        }
    }

    # Also copy to Unity Assets/Plugins/CloudflareST if the Unity project root is one level up
    $unityPluginsPath = Join-Path $ProjectRoot '..\Assets\Plugins\CloudflareST'
    if (Test-Path (Join-Path $ProjectRoot '..\Assets')) {
        if (-not (Test-Path $unityPluginsPath)) {
            New-Item -ItemType Directory -Force -Path $unityPluginsPath | Out-Null
            Write-Host "    Created Unity Plugins folder: $unityPluginsPath" -ForegroundColor DarkCyan
        }
        Copy-Item $dllPath -Destination (Join-Path $unityPluginsPath 'CloudflareST.Core.dll') -Force
        Write-Host "    Copied to Unity project: $unityPluginsPath\CloudflareST.Core.dll" -ForegroundColor Green
        foreach ($dep in $depDlls) {
            $depSrc = Join-Path $depsDir $dep
            if (Test-Path $depSrc) {
                Copy-Item $depSrc -Destination (Join-Path $unityPluginsPath $dep) -Force
                Write-Host "    Copied dep to Unity: $dep" -ForegroundColor DarkGreen
            }
        }
    } else {
        Write-Host '    Unity Assets folder not found at parent path; skipping auto-copy.' -ForegroundColor Yellow
        Write-Host "    Manually copy from: $UnityPkgPath" -ForegroundColor Yellow
    }

    Write-Host '    Unity DLL build complete.' -ForegroundColor Green
}

# ── CLI Platform Publish ──────────────────────────────────────────────────────
$Targets = @{
    'win-x64'   = @{ Rid = 'win-x64';   Exe = 'cfst.exe'; Desc = 'Windows x64' }
    'linux-x64' = @{ Rid = 'linux-x64'; Exe = 'cfst';     Desc = 'Linux x64' }
    'osx-x64'   = @{ Rid = 'osx-x64';   Exe = 'cfst';     Desc = 'macOS Intel (x64)' }
    'osx-arm64' = @{ Rid = 'osx-arm64'; Exe = 'cfst';     Desc = 'macOS Apple Silicon' }
}

function Publish-Rid {
    param([string]$Rid, [string]$Desc, [string]$Exe, [bool]$FrameworkDependent)
    $suffix = if ($FrameworkDependent) { '-fd' } else { '' }
    $OutDir = Join-Path $PublishBase "$Rid$suffix"
    $mode   = if ($FrameworkDependent) { 'fd' } else { 'self-contained' }
    Write-Host ''
    Write-Host "==> $Desc ($Rid) [$mode]" -ForegroundColor Cyan
    if ($FrameworkDependent) {
        dotnet publish "$CliProject" -r $Rid -c Release --self-contained false `
            -p:PublishSingleFile=true `
            -p:PublishTrimmed=false `
            -p:EnableCompressionInSingleFile=false `
            -o $OutDir
    } else {
        dotnet publish "$CliProject" -r $Rid -c Release --self-contained true `
            -p:PublishSingleFile=true `
            -p:PublishTrimmed=false `
            -p:EnableCompressionInSingleFile=true `
            -o $OutDir
    }
    $ExePath = Join-Path $OutDir $Exe
    if (Test-Path $ExePath) {
        $finalName = if ($Rid -eq 'win-x64') { "cfst-$Rid$suffix.exe" } else { "cfst-$Rid$suffix" }
        $FinalPath = Join-Path $PublishBase $finalName
        Copy-Item $ExePath $FinalPath -Force
        $SizeMB = [math]::Round((Get-Item $FinalPath).Length / 1048576, 2)
        Write-Host ("    Output: {0} ({1} MB)" -f $FinalPath, $SizeMB) -ForegroundColor Green
    } else {
        Write-Warning "Expected output not found: $ExePath"
    }
}

# ── Entry Point ────────────────────────────────────────────────────────────────
Push-Location $ProjectRoot
try {
    if ($unity) {
        Build-UnityDll
        exit 0
    }

    $mode = if ($fd) { 'fd' } else { 'self-contained' }
    Write-Host "CloudflareST build  |  CLI: $CliProject  [$mode]" -ForegroundColor Yellow

    if (-not (Test-Path $PublishBase)) {
        New-Item -ItemType Directory -Force -Path $PublishBase | Out-Null
    }

    if ($Platform -eq 'all') {
        foreach ($key in @('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')) {
            $t = $Targets[$key]
            Publish-Rid -Rid $t.Rid -Desc $t.Desc -Exe $t.Exe -FrameworkDependent $fd.IsPresent
        }
    } else {
        $t = $Targets[$Platform]
        Publish-Rid -Rid $t.Rid -Desc $t.Desc -Exe $t.Exe -FrameworkDependent $fd.IsPresent
    }
    Write-Host ''
    Write-Host "Done. Output: $PublishBase" -ForegroundColor Green
} finally {
    Pop-Location
}
