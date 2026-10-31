# Developer install: builds ModKit, copies it into <game>\ModKit and registers the startup hook.
# (Players use the release zip + ModKit-Install.bat instead; see build-release.ps1.)
#   .\install.ps1                  build + install ModKit
#   .\install.ps1 -WithExample     also install Examples\HelloAcademy into <game>\Mods
#   .\install.ps1 -Uninstall       remove the hook
param(
	[string]$GameDir = "E:\SteamLibrary\steamapps\common\Dungeonmans",
	[switch]$WithExample,
	[switch]$Uninstall
)
$ErrorActionPreference = "Stop"
$kitDir = Join-Path $GameDir "ModKit"

if ($Uninstall) {
	& (Join-Path $kitDir "Install-ModKit.ps1") -Uninstall
	return
}
if (Get-Process -Name Dungeonmans -ErrorAction SilentlyContinue) { throw "GAME RUNNING: close Dungeonmans first." }

dotnet build "$PSScriptRoot\ModKit\DungeonmansModKit.csproj" -c Release -p:GameDir="$GameDir" -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$out = Join-Path $PSScriptRoot "ModKit\bin\Release\net8.0"
New-Item -ItemType Directory -Force $kitDir | Out-Null
Copy-Item (Join-Path $out "DungeonmansModKit.dll"), (Join-Path $out "DungeonmansModKit.xml"), (Join-Path $out "0Harmony.dll") $kitDir -Force
Copy-Item (Join-Path $PSScriptRoot "tools\Install-ModKit.ps1"), (Join-Path $PSScriptRoot "docs\MODDING.md") $kitDir -Force
if ($WithExample) {
	New-Item -ItemType Directory -Force (Join-Path $GameDir "Mods") | Out-Null
	Copy-Item (Join-Path $PSScriptRoot "Examples\HelloAcademy") (Join-Path $GameDir "Mods") -Recurse -Force
}
& (Join-Path $kitDir "Install-ModKit.ps1")
