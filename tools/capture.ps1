# Dev only: screenshots for documentation and layout checks (see ModKit\DevCapture.cs).
# Installs ModKit and every sibling mod repo (..\dungeonmans-*\install.ps1), backs up the saves, then launches the
# game with ModKit\devcapture.txt: it loads the first save slot, captures each scene into <game>\ModKit\shots\
# and quits without saving.
#   .\tools\capture.ps1 [-Scenes all|qol_hub,poultry_birds,...]
param(
	[string]$GameDir = "E:\SteamLibrary\steamapps\common\Dungeonmans",
	[string]$Scenes = "all",
	[int]$TimeoutSec = 300
)
$ErrorActionPreference = "Stop"
if (Get-Process -Name Dungeonmans -ErrorAction SilentlyContinue) { throw "GAME RUNNING: close Dungeonmans first." }

$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $root "install.ps1") -GameDir $GameDir | Out-Null
Get-ChildItem (Split-Path $root -Parent) -Directory -Filter "dungeonmans-*" | Where-Object { $_.FullName -ne $root } | ForEach-Object {
	$inst = Join-Path $_.FullName "install.ps1"
	if (Test-Path $inst) { & $inst -GameDir $GameDir | Out-Null; Write-Host "installed $($_.Name)" }
}

$saves = Join-Path $env:APPDATA "Dungeonmans"
$backup = Join-Path $env:TEMP ("dungeonmans-save-backup-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Copy-Item $saves $backup -Recurse
Write-Host "Saves backed up to $backup"

$kit = Join-Path $GameDir "ModKit"
$shots = Join-Path $kit "shots"
if (Test-Path $shots) { Remove-Item $shots -Recurse -Force }
Set-Content (Join-Path $kit "devcapture.txt") ($Scenes -split ',') -Encoding ascii
try {
	$p = Start-Process (Join-Path $GameDir "Dungeonmans.exe") -WorkingDirectory $GameDir -PassThru
	if (-not $p.WaitForExit($TimeoutSec * 1000)) { Stop-Process -Id $p.Id -Force; Write-Host "TIMEOUT" }
}
finally {
	Remove-Item (Join-Path $kit "devcapture.txt") -ErrorAction SilentlyContinue
}
Get-Content (Join-Path $kit "modkit.log") | Select-String "DevCapture|FAIL|threw"
if (Test-Path $shots) { Get-ChildItem $shots | ForEach-Object { $_.FullName } }
