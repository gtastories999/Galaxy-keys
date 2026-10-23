$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet run --project tests/GalaxyKeys.Tests
    if ($LASTEXITCODE -ne 0) { throw 'Core checks failed.' }
    dotnet publish src/GalaxyKeys -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Get-FileHash dist/GalaxyKeys.exe -Algorithm SHA256 | Format-List
} finally { Pop-Location }
