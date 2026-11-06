# Registers the Open Inventor Export add-in for the current Windows user, for testing.
# Writes one .addin file per Inventor version into that version's per-user add-in folder
# (%AppData%\Autodesk\Inventor <version>\Addins, listed in Autodesk's SDK SimpleAddIn Readme).
# Each file points at the matching build in this folder's bin\<Configuration>:
#   Inventor 2024 -> net48 build, Inventor 2027 -> net8.0-windows build.
# A .addin file in a version's own folder loads only in that version.
#
# Usage (Inventor closed):  .\install-addin.ps1            install / update
#                           .\install-addin.ps1 -Uninstall  remove the .addin files again
param(
    [string]$Configuration = "Debug",
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
$classId = "{0CC8A77E-188C-46AD-8D2A-E9DE6BC374D6}"   # same as AddInServer.ClassId and the manifest
$fileName = "OpenInventorExport.AddIn.addin"

$targets = @(
    @{ Version = "2024"; Framework = "net48" },
    @{ Version = "2027"; Framework = "net8.0-windows" }
)

foreach ($t in $targets) {
    $folder = Join-Path $env:APPDATA "Autodesk\Inventor $($t.Version)\Addins"
    $addinPath = Join-Path $folder $fileName

    if ($Uninstall) {
        if (Test-Path $addinPath) { Remove-Item $addinPath; "Removed $addinPath" } else { "Not installed for $($t.Version)" }
        continue
    }

    $dll = Join-Path $PSScriptRoot "bin\$Configuration\$($t.Framework)\OpenInventorExport.AddIn.dll"
    if (-not (Test-Path $dll)) { "SKIPPED Inventor $($t.Version): build first, $dll not found"; continue }

    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<!-- Written by install-addin.ps1. Shape copied from Autodesk's SDK SimpleAddIn sample. -->
<Addin Type="Standard">
  <ClassId>$classId</ClassId>
  <ClientId>$classId</ClientId>
  <DisplayName>Open Inventor Export</DisplayName>
  <Description>Exports assemblies to component lists and files (read-only).</Description>
  <Assembly>$dll</Assembly>
  <LoadOnStartUp>1</LoadOnStartUp>
  <Hidden>0</Hidden>
</Addin>
"@
    New-Item -ItemType Directory -Force $folder | Out-Null
    [IO.File]::WriteAllText($addinPath, $xml, (New-Object Text.UTF8Encoding($false)))
    "Inventor $($t.Version): $addinPath -> $dll"
}
