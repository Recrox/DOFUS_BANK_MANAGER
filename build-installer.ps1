# Construit l'installateur Windows : dist\DofusRetroManager-Setup.msi
# Usage (depuis la racine du projet) :  .\build-installer.ps1 [-Version 1.4.2]
# Sans -Version, la version du .csproj est utilisée (le workflow de Release passe la version calculée).
param([string]$Version)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$payload = Join-Path $root 'installer\payload'

# 1. Application autonome (.NET inclus) en un seul exécutable : aucun prérequis chez l'utilisateur
if (Test-Path $payload) { Remove-Item -Recurse -Force $payload }
Remove-Item "$root\DofusRetroManager\*_wpftmp.csproj" -ErrorAction SilentlyContinue # restes d'une compilation WPF interrompue
# Syntaxe /p: : Windows PowerShell 5.1 découpe les arguments natifs de la forme -p:Nom=valeur
# Tableau construit explicitement : « $x = if (…) { @('a') } » le réduirait à une chaîne, que PowerShell 7
# éclaterait ensuite en caractères (« / p : V e r s i o n … »). Un tableau passé à un programme natif
# donne un argument par élément.
$versionArgs = @()
if ($Version) { $versionArgs += "/p:Version=$Version" }
dotnet publish "$root\DofusRetroManager\DofusRetroManager.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $payload $versionArgs
if ($LASTEXITCODE -ne 0) { throw "Échec de la publication de l'application" }

# 2. Paquet MSI (WiX Toolset, récupéré automatiquement via NuGet)
dotnet build "$root\installer\DofusRetroManager.Installer.wixproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "Échec de la construction de l'installateur" }

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item "$root\installer\bin\Release\DofusRetroManager-Setup.msi" -Destination $dist
Get-ChildItem $dist -Filter *.msi | Select-Object FullName, @{ n = 'Taille (Mo)'; e = { [math]::Round($_.Length / 1MB, 1) } }
