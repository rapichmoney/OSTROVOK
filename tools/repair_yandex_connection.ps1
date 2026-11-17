$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$exe = @((Join-Path $projectRoot 'release\DesktopIsland.exe'), (Join-Path $projectRoot 'DesktopIsland.exe')) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$exe) { throw 'DesktopIsland.exe not found next to the repair tool' }
$report = Join-Path $env:TEMP 'DesktopIsland-yandex-repair.json'
$process = Start-Process -FilePath $exe -ArgumentList "--repair-yandex --report `"$report`"" -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw 'Yandex launch repair failed' }
Get-Content -LiteralPath $report -Raw
