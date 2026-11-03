# Builds dist\DungeonmansModKit-<v>.zip, ready for players and modders (no build tools needed on their side):
# extract into the Dungeonmans game folder and double-click ModKit-Install.bat.
param([string]$GameDir = "E:\SteamLibrary\steamapps\common\Dungeonmans")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

dotnet build "$root\ModKit\DungeonmansModKit.csproj" -c Release -p:GameDir="$GameDir" -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$out = Join-Path $root "ModKit\bin\Release\net8.0"
$version = (Get-Item (Join-Path $out "DungeonmansModKit.dll")).VersionInfo.ProductVersion -replace '\+.*', ''
$dist = Join-Path $root "dist"
$stage = Join-Path $dist "stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$kit = Join-Path $stage "ModKit"
New-Item -ItemType Directory -Force $kit | Out-Null

Copy-Item (Join-Path $out "DungeonmansModKit.dll"), (Join-Path $out "DungeonmansModKit.xml"), (Join-Path $out "0Harmony.dll") $kit
Copy-Item (Join-Path $root "tools\Install-ModKit.ps1"), (Join-Path $root "docs\MODDING.md"), (Join-Path $root "LICENSE") $kit
Copy-Item (Join-Path $root "Examples") $kit -Recurse
Get-ChildItem $kit -Recurse -Directory -Filter ".cache" | Remove-Item -Recurse -Force
foreach ($f in "ModKit-Install.bat", "ModKit-Uninstall.bat", "ModKit-README.txt") {
	Copy-Item (Join-Path $root "tools\$f") $stage
}

$zip = Join-Path $dist "DungeonmansModKit-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Remove-Item $stage -Recurse -Force
Write-Host "-> $zip"
