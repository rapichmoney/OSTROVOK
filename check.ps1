$ErrorActionPreference = 'Stop'
node "$PSScriptRoot\tools\verify_yandex_adapter.mjs"
if ($LASTEXITCODE -ne 0) { throw 'Yandex adapter checks failed' }
node "$PSScriptRoot\tools\verify_youtube_controls.mjs"
if ($LASTEXITCODE -ne 0) { throw 'YouTube control checks failed' }
dotnet build "$PSScriptRoot\DesktopIsland.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$testExe = Join-Path $PSScriptRoot 'bin\Release\net9.0-windows10.0.19041.0\DesktopIsland.exe'
$reportFolder = Join-Path $PSScriptRoot 'verification'
New-Item -ItemType Directory -Path $reportFolder -Force | Out-Null
$coreReportPath = Join-Path $reportFolder 'core-report.json'
$coreProcess = Start-Process -FilePath $testExe -ArgumentList "--self-test --report `"$coreReportPath`"" -WindowStyle Hidden -PassThru -Wait
if ($coreProcess.ExitCode -ne 0) { throw "Core checks failed: $coreReportPath" }
$uiFolder = Join-Path $reportFolder 'ui'
$uiProcess = Start-Process -FilePath $testExe -ArgumentList "--verify-ui --output `"$uiFolder`"" -WindowStyle Hidden -PassThru -Wait
if ($uiProcess.ExitCode -ne 0) { throw "UI process exited with code $($uiProcess.ExitCode). Check fresh UI report or Windows Application events: $uiFolder\ui-report.json" }
$coreReport = Get-Content -LiteralPath $coreReportPath -Raw | ConvertFrom-Json
$uiReport = Get-Content -LiteralPath "$uiFolder\ui-report.json" -Raw | ConvertFrom-Json
if ($coreReport.failures -ne 0 -or $uiReport.failures -ne 0) { throw 'Verification report contains failures' }
Write-Output "Passed: $($coreReport.checks.Count + $uiReport.checks.Count) checks. Reports: $reportFolder"
