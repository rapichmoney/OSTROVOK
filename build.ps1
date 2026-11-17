param([string]$OutputFolder = "$PSScriptRoot\artifacts\portable")
$ErrorActionPreference = 'Stop'
dotnet publish "$PSScriptRoot\DesktopIsland.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $OutputFolder
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Copy-Item -LiteralPath "$PSScriptRoot\README.md" -Destination $OutputFolder
Copy-Item -LiteralPath "$PSScriptRoot\LICENSE" -Destination $OutputFolder
Copy-Item -LiteralPath "$PSScriptRoot\THIRD-PARTY-NOTICES.md" -Destination $OutputFolder
Copy-Item -LiteralPath "$PSScriptRoot\Licenses" -Destination $OutputFolder -Recurse -Force
New-Item -ItemType Directory -Path "$OutputFolder\tools" -Force | Out-Null
Copy-Item -LiteralPath "$PSScriptRoot\tools\repair_yandex_connection.ps1" -Destination "$OutputFolder\tools\repair_yandex_connection.ps1" -Force
Copy-Item -LiteralPath "$PSScriptRoot\Package" -Destination $OutputFolder -Recurse -Force
$archivePath = Join-Path (Split-Path $OutputFolder -Parent) 'OSTROVOK-1.0.0-win-x64.zip'
Compress-Archive -Path "$OutputFolder\*" -DestinationPath $archivePath -Force
Get-Item -LiteralPath $archivePath
