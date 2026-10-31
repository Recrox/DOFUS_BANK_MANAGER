# Construit l'installateur Windows : dist\DofusBankManager-Setup.msi
# Usage (depuis la racine du projet) :  .\build-installer.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$payload = Join-Path $root 'installer\payload'

# 1. Application autonome (.NET inclus) en un seul exécutable : aucun prérequis chez l'utilisateur
if (Test-Path $payload) { Remove-Item -Recurse -Force $payload }
dotnet publish "$root\DofusBankManager" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $payload
if ($LASTEXITCODE -ne 0) { throw "Échec de la publication de l'application" }

# 2. Paquet MSI (WiX Toolset, récupéré automatiquement via NuGet)
dotnet build "$root\installer\DofusBankManager.Installer.wixproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "Échec de la construction de l'installateur" }

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item "$root\installer\bin\Release\DofusBankManager-Setup.msi" -Destination $dist
Get-ChildItem $dist -Filter *.msi | Select-Object FullName, @{ n = 'Taille (Mo)'; e = { [math]::Round($_.Length / 1MB, 1) } }
