param(
    [ValidateSet('Build','Test','Publish')][string]$Action = 'Build',
    [string]$SampleDirectory,
    [string]$ManualResourceDirectory,
    [string]$PublishDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.local\dotnet'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.local\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
Push-Location $projectRoot
try {
    switch ($Action) {
        'Build' { dotnet build -c Release }
        'Test' {
            $env:FIVEM_INSTALLER_TEST_ROOT = Join-Path $projectRoot '.local\test-workspaces'
            $env:FIVEM_UI_PREVIEW_ROOT = Join-Path $projectRoot 'artifacts\preview'
            if ($SampleDirectory) { $env:FIVEM_VEHICLE_SAMPLE = (Resolve-Path -LiteralPath $SampleDirectory).Path }
            if ($ManualResourceDirectory) { $env:FIVEM_MANUAL_VEHICLE_RESOURCE = (Resolve-Path -LiteralPath $ManualResourceDirectory).Path }
            dotnet test -c Release --logger 'trx;LogFileName=validation.trx' --results-directory artifacts\test-results
        }
        'Publish' {
            if (-not $PublishDirectory) { $PublishDirectory = Join-Path $projectRoot 'artifacts\portable\win-x64' }
            $PublishDirectory = [IO.Path]::GetFullPath($PublishDirectory)
            dotnet publish src\FiveMVehicleInstaller.App\FiveMVehicleInstaller.App.csproj -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -p:PublishAot=false -o $PublishDirectory
            if ($LASTEXITCODE -ne 0) { throw 'Falha na publicação.' }
            Copy-Item -LiteralPath README.md,VALIDATION.md,PACKS.md -Destination $PublishDirectory
            Compress-Archive -Path (Join-Path $PublishDirectory '*') -DestinationPath artifacts\FiveMVehicleInstaller-win-x64.zip -Force
            $zipPath = Join-Path $projectRoot 'artifacts\FiveMVehicleInstaller-win-x64.zip'
            $checksum = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant() + '  FiveMVehicleInstaller-win-x64.zip'
            Set-Content -LiteralPath ($zipPath + '.sha256') -Value $checksum -Encoding ASCII
            Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts\FiveMVehicleInstaller-win-x64.sha256') -Value $checksum -Encoding ASCII
        }
    }
    if ($LASTEXITCODE -ne 0) { throw "dotnet terminou com código $LASTEXITCODE." }
}
finally { Pop-Location }
