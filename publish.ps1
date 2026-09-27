# Portable, zero-dependency paket üretir: tek dosya exe + yanındaki Resources klasörü.
# Kullanım:  .\publish.ps1            veya   .\publish.ps1 -Version 1.1.0
param(
    [string]$Version = "1.0.0",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$output = Join-Path $root "publish\DataModelGenerator-v$Version-$Runtime"

Write-Host "Yayın hazırlanıyor: $output"

if (Test-Path $output) { Remove-Item $output -Recurse -Force }

dotnet publish (Join-Path $root "DataModelGenerator.App\DataModelGenerator.App.csproj") `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $output `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none

if ($LASTEXITCODE -ne 0) { throw "dotnet publish başarısız oldu." }

# Uygulama log dosyalarını exe'nin yanına yazar.
New-Item -ItemType Directory -Force -Path (Join-Path $output "logs") | Out-Null

$zip = Join-Path $root "publish\DataModelGenerator-v$Version-$Runtime.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $output "*") -DestinationPath $zip

$sizeMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ""
Write-Host "Klasör : $output"
Write-Host "Arşiv  : $zip ($sizeMb MB)"
Write-Host ""
Write-Host "Dağıtım notu: .NET kurulumu gerekmez. Mermaid diyagramı için Windows'ta"
Write-Host "hazır gelen WebView2 çalışma zamanı kullanılır; yoksa diyagram sekmesi"
Write-Host "uyarı gösterir, Mermaid kodu sekmesi ve dosya dışa aktarımı çalışmaya devam eder."
