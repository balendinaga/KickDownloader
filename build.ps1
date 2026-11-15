<#
.SYNOPSIS
  Runs the tests, then publishes kickdl.exe (command line) and KickDownloaderWPF.exe (desktop app)
  as self-contained single files into ./dist.
.PARAMETER FrameworkDependent
  Produce small exes that need the .NET 10 (Desktop) runtime installed instead of self-contained ones.
.PARAMETER SkipTests
  Do not run the unit tests first.
#>
param(
    [switch]$FrameworkDependent,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not $SkipTests) {
    dotnet test --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed, not publishing.' }
}

$dist = Join-Path $PSScriptRoot 'dist'
if (Test-Path $dist) {
    # Only remove what a previous publish produced inside dist, never anything outside it.
    Get-ChildItem -LiteralPath $dist -Force | Remove-Item -Recurse -Force
}

$self = if ($FrameworkDependent) { 'false' } else { 'true' }
foreach ($project in 'src/KickDownloaderCLI', 'src/KickDownloaderWPF') {
    dotnet publish $project -c Release -r win-x64 --self-contained $self `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:CopyOutputSymbolsToPublishDirectory=false -o $dist --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
}

Remove-Item -Path (Join-Path $dist '*.pdb') -Force -ErrorAction SilentlyContinue   # symbols shipped inside NuGet packages
# Licenses must travel with the binaries (TD-License requires its notice to be included with compiled binaries).
$render = 'src/KickDownloaderCore/Render'
Copy-Item README.md, LICENSE, THIRD_PARTY_NOTICES.md, "$render/Resources/TD-License", "$render/Resources/THIRD-PARTY-LICENSES.txt", "$render/LICENSE-TwitchDownloader.txt" -Destination $dist
Get-ChildItem $dist | Select-Object Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
