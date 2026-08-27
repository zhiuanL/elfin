[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$sdkRoot = Join-Path $repoRoot '.tools/dotnet'
$dotnetExe = Join-Path $sdkRoot 'dotnet.exe'
if (Test-Path -LiteralPath $dotnetExe) {
    $env:DOTNET_ROOT = $sdkRoot
} else {
    $dotnetExe = (Get-Command dotnet -ErrorAction Stop).Source
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
Push-Location -LiteralPath $repoRoot
try {
    & $dotnetExe restore DesktopPet.sln --locked-mode --packages .packages
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
    & $dotnetExe build DesktopPet.sln --no-restore --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
    & $dotnetExe test DesktopPet.sln --no-build --no-restore --configuration $Configuration --logger 'trx;LogFilePrefix=phase-0' --results-directory "artifacts/TestResults/$Configuration"
    if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }
}
finally { Pop-Location }
