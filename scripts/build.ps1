# CloudflareST multi-platform build script
# Usage: .\build.ps1 [platform] [-fd]
# Example: .\build.ps1         # build all (self-contained)
#          .\build.ps1 -fd    # build all (framework-dependent, ~几百 KB)

param(
    [Parameter(Position = 0)]
    [ValidateSet('all', 'win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')]
    [string]$Platform = 'all',
    [switch]$fd
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
        $Size = (Get-Item $ExePath).Length / 1MB
        Write-Host "    Output: $OutDir\$Exe ($([math]::Round($Size, 2)) MB)" -ForegroundColor Green
    }
}

Push-Location $ProjectRoot
try {
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
} finally {
    Pop-Location
}
