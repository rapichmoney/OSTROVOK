param([Parameter(Mandatory=$true)][string]$PlanFile)
$ErrorActionPreference = 'Stop'
$plan = Get-Content -LiteralPath $PlanFile -Raw -Encoding UTF8 | ConvertFrom-Json
$stage = [IO.Path]::GetFullPath($plan.stage).TrimEnd('\')
$target = [IO.Path]::GetFullPath($plan.target).TrimEnd('\')
$backup = Join-Path (Split-Path $stage -Parent) 'backup'
$resultPath = Join-Path (Split-Path $stage -Parent) 'result.json'
$installed = New-Object 'System.Collections.Generic.List[object]'
try {
    if ($stage -eq $target -or !(Test-Path -LiteralPath (Join-Path $stage 'DesktopIsland.exe')) -or !(Test-Path -LiteralPath (Join-Path $target 'DesktopIsland.exe'))) { throw 'Invalid update folders' }
    if ($plan.pid -gt 0) {
        $old = Get-Process -Id $plan.pid -ErrorAction SilentlyContinue
        if ($old -and [Math]::Abs(($old.StartTime.ToUniversalTime() - [DateTime]::Parse($plan.started).ToUniversalTime()).TotalSeconds) -gt 2) { throw 'Application process changed' }
        if ($old -and !$old.WaitForExit(30000)) { throw 'Application did not exit' }
    }
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $stage -File -Recurse) {
        $relative = $file.FullName.Substring($stage.Length + 1)
        $destination = [IO.Path]::GetFullPath((Join-Path $target $relative))
        if (!$destination.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid destination' }
        $saved = Join-Path $backup $relative
        $existed = Test-Path -LiteralPath $destination
        if ($existed) { New-Item -ItemType Directory -Path (Split-Path $saved -Parent) -Force | Out-Null; Copy-Item -LiteralPath $destination -Destination $saved -Force }
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        $installed.Add([pscustomobject]@{path=$destination;backup=$saved;existed=$existed})
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
    @{success=$true;version=$plan.version} | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
    if ($plan.restart) { Start-Process -FilePath (Join-Path $target 'DesktopIsland.exe') -ArgumentList '--updated', $plan.version -WindowStyle Hidden }
} catch {
    $failure = $_.Exception.Message
    for ($i=$installed.Count-1; $i -ge 0; $i--) {
        $file=$installed[$i]
        try { if ($file.existed) { Copy-Item -LiteralPath $file.backup -Destination $file.path -Force } else { Remove-Item -LiteralPath $file.path -Force } } catch { }
    }
    @{success=$false;error=$failure} | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
    if ($plan.restart) { Start-Process -FilePath (Join-Path $target 'DesktopIsland.exe') -ArgumentList '--update-failed' -WindowStyle Hidden }
    exit 1
}
