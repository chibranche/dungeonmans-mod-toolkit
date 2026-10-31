# Dungeonmans ModKit installer. Lives in <game>\ModKit\ and is run by ModKit-Install.bat / ModKit-Uninstall.bat.
# Registers ModKit as a .NET startup hook in Dungeonmans.runtimeconfig.json (the only game file it touches).
# Run it again after a Steam update or "verify game files": those restore the original runtimeconfig.
#   Install-ModKit.ps1              install / repair
#   Install-ModKit.ps1 -Uninstall   remove the hook (mods stay on disk but no longer load)
param([switch]$Uninstall)
$ErrorActionPreference = "Stop"

$kitDir = $PSScriptRoot
$gameDir = Split-Path $kitDir -Parent
$config = Join-Path $gameDir "Dungeonmans.runtimeconfig.json"
$hookDll = Join-Path $kitDir "DungeonmansModKit.dll"

if (-not (Test-Path (Join-Path $gameDir "Dungeonmans.exe")) -or -not (Test-Path $config)) {
	throw "Dungeonmans.exe not found in $gameDir. Extract the zip into the Dungeonmans game folder (Steam: right-click the game > Manage > Browse local files)."
}
if (Get-Process -Name Dungeonmans -ErrorAction SilentlyContinue) {
	throw "Dungeonmans is running. Close the game first."
}

$json = Get-Content $config -Raw | ConvertFrom-Json
$props = $json.runtimeOptions.configProperties
$hasHook = $props.PSObject.Properties.Name -contains "STARTUP_HOOKS"

if ($Uninstall) {
	if ($hasHook) {
		$props.PSObject.Properties.Remove("STARTUP_HOOKS")
		[System.IO.File]::WriteAllText($config, ($json | ConvertTo-Json -Depth 10))
	}
	Write-Host "ModKit uninstalled: the game starts without mods. (The ModKit and Mods folders are left in place; delete them if you like.)"
	return
}

if (-not (Test-Path $hookDll)) { throw "Missing $hookDll" }

# Keep a copy of the untouched file (refreshed whenever the current one has no hook, e.g. after a Steam update).
if (-not $hasHook) { Copy-Item $config "$config.vanilla" -Force }

if ($hasHook) { $props.STARTUP_HOOKS = $hookDll }
else { $props | Add-Member -NotePropertyName STARTUP_HOOKS -NotePropertyValue $hookDll }
# UTF-8 without BOM: the .NET host is picky about the runtimeconfig.
[System.IO.File]::WriteAllText($config, ($json | ConvertTo-Json -Depth 10))

$mods = Join-Path $gameDir "Mods"
New-Item -ItemType Directory -Force $mods | Out-Null

# One-time migration from the pre-ModKit QoL mod (<game>\QoLMod): keep its settings, then retire the folder.
$oldQoL = Join-Path $gameDir "QoLMod"
$newQoL = Join-Path $mods "QoL"
if ((Test-Path $oldQoL) -and (Test-Path $newQoL)) {
	foreach ($f in "config.json", "proof_presets.json", "map_filters.json") {
		$src = Join-Path $oldQoL $f
		if ((Test-Path $src) -and -not (Test-Path (Join-Path $newQoL $f))) { Copy-Item $src $newQoL }
	}
	$retired = "$oldQoL.old"
	if (Test-Path $retired) { Remove-Item $retired -Recurse -Force }
	Rename-Item $oldQoL (Split-Path $retired -Leaf)
	Write-Host "Moved your old QoL settings to Mods\QoL (old folder kept as QoLMod.old)."
}

$list = Get-ChildItem $mods -Directory | ForEach-Object { $_.Name }
Write-Host "ModKit installed. Mods found: $(if ($list) { $list -join ', ' } else { 'none yet' })"
Write-Host "Log after launching: $kitDir\modkit.log"
