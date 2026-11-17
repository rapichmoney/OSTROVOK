param([string]$AppFolder = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$appRoot = [IO.Path]::GetFullPath($AppFolder)
if (!(Test-Path -LiteralPath (Join-Path $appRoot 'DesktopIsland.exe'))) { throw 'DesktopIsland.exe was not found in the application directory.' }
$registered = Get-AppxPackage -Name 'DesktopIsland.Desktop'
if ($registered -and $registered.InstallLocation -eq $PSScriptRoot) { Write-Output 'Already registered'; exit 0 }
# Developer registration uses the existing developer setting; this script never changes it or certificate stores.
$developer = Get-ItemPropertyValue -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue
if ($developer -ne 1) { throw 'A signed Windows package is required on this computer. Developer mode and certificate stores were not changed.' }
Add-AppxPackage -Register (Join-Path $PSScriptRoot 'AppxManifest.xml') -ExternalLocation $appRoot
Write-Output 'Desktop Island identity registered'
