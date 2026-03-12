# CloudflareST multi-platform build script
# Usage: .\build.ps1 [platform] [-fd] [-clean]
# Example: .\build.ps1              # build all (self-contained)
#          .\build.ps1 -fd         # build all (framework-dependent)
#          .\build.ps1 -clean      # clean obj/bin/publish only
#          .\build.ps1 win-x64 -clean  # clean then build win-x64

param(
    [Parameter(Position = 0)]
    [ValidateSet('all', 'win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')]
    [string]$Platform = 'all',
    [switch]$fd,
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
    param([string]$Rid, [string]$Desc, [string]$Exe, [bool]$FrameworkDependent)
    $suffix = ''
    if ($FrameworkDependent) { $suffix = '-fd' }
    $OutDir = Join-Path $PublishBase "$Rid$suffix"
    $mode = "self-contained"
    if ($FrameworkDependent) { $mode = "fd" }
    Write-Host "`n==> $Desc ($Rid) [$mode]" -ForegroundColor Cyan
    if ($FrameworkDependent) {
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
        $mode = "self-contained"
        if ($fd) { $mode = "fd" }
        Write-Host "CloudflareST build - Project: $ProjectRoot [$mode]" -ForegroundColor Yellow
        if ($Platform -eq 'all') {
            foreach ($key in @('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')) {
                $t = $Targets[$key]
                Publish-Rid -Rid $t.Rid -Desc $t.Desc -Exe $t.Exe -FrameworkDependent $fd.IsPresent
            }
        } else {
            $t = $Targets[$Platform]
            Publish-Rid -Rid $t.Rid -Desc $t.Desc -Exe $t.Exe -FrameworkDependent $fd.IsPresent
        }
        Write-Host "`nDone. Output: $PublishBase" -ForegroundColor Green
    }
} finally {
    Pop-Location
}
