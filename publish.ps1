$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

dotnet publish src/WirelessBattery/WirelessBattery.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o dist

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$exe = Join-Path $PSScriptRoot "dist\WirelessBattery.exe"
Write-Host "已生成 $exe"
Write-Host "把这个文件发给朋友，双击即可打开。"
