param(
  [string]$RID = "win-x64",
  [string]$OutputDir = "publish"
)
Set-StrictMode -Version Latest
Write-Host "Publishing Core CLI for RID=$RID to $OutputDir" -ForegroundColor Green
dotnet publish CloudflareST.Cli/CloudflareST.Cli.csproj -c Release -r $RID --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o "$OutputDir/$RID"
