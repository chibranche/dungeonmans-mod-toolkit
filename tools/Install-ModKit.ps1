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
if (-not $json.runtimeOptions.configProperties) {
	$json.runtimeOptions | Add-Member -NotePropertyName configProperties -NotePropertyValue ([pscustomobject]@{}) -Force
}
$props = $json.runtimeOptions.configProperties
$hasHook = $props.PSObject.Properties.Name -contains "STARTUP_HOOKS"
# STARTUP_HOOKS is a ";" list: other tools' hooks are kept, only ModKit's own entry (any folder) is replaced.
$others = @()
if ($hasHook) { $others = @("$($props.STARTUP_HOOKS)" -split ";" | Where-Object { $_ -and ($_ -notlike "*DungeonmansModKit.dll") }) }

function Save-Config {
	# UTF-8 without BOM: the .NET host is picky about the runtimeconfig.
	[System.IO.File]::WriteAllText($config, ($json | ConvertTo-Json -Depth 10))
}

if ($Uninstall) {
	if ($hasHook) {
		if ($others.Count -gt 0) { $props.STARTUP_HOOKS = $others -join ";" }
		else { $props.PSObject.Properties.Remove("STARTUP_HOOKS") }
		Save-Config
	}
	Write-Host "ModKit uninstalled: the game starts without mods. (The ModKit and Mods folders are left in place; delete them if you like.)"
	return
}

if (-not (Test-Path $hookDll)) { throw "Missing $hookDll" }

# Keep a copy of the untouched file (refreshed whenever the current one has no hook, e.g. after a Steam update).
if (-not $hasHook) { Copy-Item $config "$config.vanilla" -Force }

$value = (@($others) + $hookDll) -join ";"
if ($hasHook) { $props.STARTUP_HOOKS = $value }
else { $props | Add-Member -NotePropertyName STARTUP_HOOKS -NotePropertyValue $value }
Save-Config

$mods = Join-Path $gameDir "Mods"
New-Item -ItemType Directory -Force $mods | Out-Null

$list = Get-ChildItem $mods -Directory | ForEach-Object { $_.Name }
Write-Host "ModKit installed. Mods found: $(if ($list) { $list -join ', ' } else { 'none yet' })"
Write-Host "Log after launching: $kitDir\modkit.log"
