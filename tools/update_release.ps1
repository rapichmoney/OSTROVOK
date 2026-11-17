param([switch]$ConnectTelegram, [ValidateSet('Segoe UI', 'Aminahs Schoolbook Sans Serif Bd')][string]$InterfaceFont, [string]$DiscordApplicationId)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$target = [IO.Path]::GetFullPath((Join-Path $projectRoot 'release\DesktopIsland.exe'))
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type 'using System; using System.Runtime.InteropServices; public static class IslandReleaseUpdate { [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l); }'
foreach ($appProcess in (Get-Process DesktopIsland -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $target })) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $appProcess.Id)
    $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
    $window = @($windows | Where-Object { $_.Current.Name -eq 'Desktop Island' }) | Select-Object -First 1
    if ($window) { [IslandReleaseUpdate]::PostMessage([IntPtr]$window.Current.NativeWindowHandle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
    if (!$appProcess.WaitForExit(8000)) { throw 'Island has not finished saving and closing' }
}
if (Test-Path -LiteralPath $target) {
    $oldVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($target).FileVersion
    if ($oldVersion -match '^\d+(\.\d+){3}$') {
        $backup = Join-Path (Split-Path $target -Parent) ('DesktopIsland-' + $oldVersion + '.exe')
        if (!(Test-Path -LiteralPath $backup)) { Copy-Item -LiteralPath $target -Destination $backup }
    }
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'artifacts\portable\DesktopIsland.exe') -Destination $target -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $projectRoot 'release\README.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $projectRoot 'release\THIRD-PARTY-NOTICES.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'Licenses') -Destination (Join-Path $projectRoot 'release') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'Assets\Fonts\OFL-Aminahs.txt') -Destination (Join-Path $projectRoot 'release\OFL-Aminahs.txt') -Force
New-Item -ItemType Directory -Path (Join-Path $projectRoot 'release\tools') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'tools\repair_yandex_connection.ps1') -Destination (Join-Path $projectRoot 'release\tools\repair_yandex_connection.ps1') -Force
if ($InterfaceFont -or $DiscordApplicationId) {
    $settingsPath = Join-Path $env:LOCALAPPDATA 'DesktopIsland\settings.json'
    $config = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    if ($InterfaceFont) { $config | Add-Member -NotePropertyName InterfaceFont -NotePropertyValue $InterfaceFont -Force }
    if ($DiscordApplicationId) {
        $config | Add-Member -NotePropertyName DiscordApplicationId -NotePropertyValue $DiscordApplicationId -Force
        $config | Add-Member -NotePropertyName DiscordPresenceEnabled -NotePropertyValue $true -Force
    }
    $config | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
}
if ($ConnectTelegram) { $app = Start-Process -FilePath $target -ArgumentList '--connect-telegram' -WindowStyle Hidden -PassThru }
else { $app = Start-Process -FilePath $target -WindowStyle Hidden -PassThru }
Start-Sleep -Seconds 2
Get-Process -Id $app.Id | Select-Object Id, Responding, @{n='Version';e={$_.MainModule.FileVersionInfo.FileVersion}}
