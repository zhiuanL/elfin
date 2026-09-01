[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnetExe = Join-Path $repoRoot '.tools/dotnet/dotnet.exe'
if (Test-Path -LiteralPath $dotnetExe) { $env:DOTNET_ROOT = Split-Path -Parent $dotnetExe }
else { $dotnetExe = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_HOST_PATH = $dotnetExe
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location -LiteralPath $repoRoot
try {
    & $dotnetExe restore DesktopPet.sln --locked-mode --packages .packages
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
    & $dotnetExe build DesktopPet.sln -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
    & $dotnetExe test DesktopPet.sln -c $Configuration --no-build --no-restore --logger 'trx;LogFilePrefix=phase-5' --results-directory "artifacts/TestResults/$Configuration"
    if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }
}
finally { Pop-Location }
