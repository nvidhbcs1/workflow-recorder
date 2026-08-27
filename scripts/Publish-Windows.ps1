[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$distRoot = Join-Path $projectRoot 'dist'
$appOutput = Join-Path $distRoot "$Runtime\app"
$cliOutput = Join-Path $distRoot "$Runtime\cli"

dotnet publish (Join-Path $projectRoot 'src\WorkflowRecorder.App\WorkflowRecorder.App.csproj') `
    -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None `
    -o $appOutput

dotnet publish (Join-Path $projectRoot 'src\WorkflowRecorder.Cli\WorkflowRecorder.Cli.csproj') `
    -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None `
    -o $cliOutput

Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $distRoot "$Runtime\README.md") -Force
$archivePath = Join-Path $distRoot "WorkflowRecorder-$Runtime.zip"
Compress-Archive -Path (Join-Path $distRoot "$Runtime\*") -DestinationPath $archivePath -CompressionLevel Optimal -Force
Write-Host "Portable package created at $distRoot\$Runtime"
Write-Host "Portable ZIP created at $archivePath"
