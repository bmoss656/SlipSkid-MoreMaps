# Builds a Release DLL and packs the Thunderstore zip into dist/.
# Usage: powershell -ExecutionPolicy Bypass -File tools\package.ps1
# Layout (files at the zip root, as Thunderstore requires):
#   manifest.json, icon.png, README.md, CHANGELOG.md
#   BepInEx/plugins/MoreMaps/SlipSkid.MoreMaps.dll + routes/*.json
param([string]$Repo = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = "Stop"

$manifest = Get-Content (Join-Path $Repo "thunderstore\manifest.json") -Raw | ConvertFrom-Json
$version = $manifest.version_number
$pluginVersion = [regex]::Match((Get-Content (Join-Path $Repo "src\Plugin.cs") -Raw), 'Version = "([^"]+)"').Groups[1].Value
if ($pluginVersion -ne $version) { throw "Plugin.Version ($pluginVersion) and manifest version_number ($version) differ" }
if ($manifest.name -notmatch '^[A-Za-z0-9_]+$') { throw "manifest name may only use letters, digits and underscores" }
if ($manifest.description.Length -gt 250) { throw "manifest description is over 250 characters" }

# Build into a staging folder rather than the game's plugins folder.
$stage = Join-Path $Repo "dist\$($manifest.name)"
$plugin = Join-Path $stage "BepInEx\plugins\MoreMaps"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $plugin | Out-Null
dotnet build (Join-Path $Repo "SlipSkid.MoreMaps.csproj") -c Release -nologo -v q "-p:PluginDir=$plugin"
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$routes = @(Get-ChildItem (Join-Path $plugin "routes") -Filter *.json)
if ($routes.Count -eq 0) { throw "no routes in the package" }

foreach ($file in "manifest.json", "icon.png", "README.md", "CHANGELOG.md") {
    Copy-Item (Join-Path $Repo "thunderstore\$file") $stage
}
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Image]::FromFile((Join-Path $stage "icon.png"))
$iconSize = "$($icon.Width)x$($icon.Height)"; $icon.Dispose()
if ($iconSize -ne "256x256") { throw "icon.png must be 256x256 (is $iconSize)" }

$zip = Join-Path $Repo "dist\$($manifest.name)-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
# Zip the folder's contents (not the folder), forward-slash paths for mod managers.
$archive = [System.IO.Compression.ZipFile]::Open($zip, "Create")
try {
    Get-ChildItem $stage -Recurse -File | ForEach-Object {
        $entry = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entry, "Optimal") | Out-Null
    }
}
finally { $archive.Dispose() }
"wrote $zip ($([math]::Round((Get-Item $zip).Length / 1KB, 1)) KB, routes: $(($routes | ForEach-Object Name) -join ', '))"
