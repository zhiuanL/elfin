#Requires -Version 5.1
<#
.SYNOPSIS
Build and start elfin using the repository SDK when available.
.EXAMPLE
.\tools\Start-Elfin.ps1
.EXAMPLE
.\tools\Start-Elfin.ps1 -Configuration Release
.EXAMPLE
.\tools\Start-Elfin.ps1 -NoBuild -Installed
.EXAMPLE
.\tools\Start-Elfin.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$NoBuild,
    [switch]$Installed
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src/DesktopPet.App/DesktopPet.App.csproj'
$outputDirectory = Join-Path $repoRoot "src/DesktopPet.App/bin/$Configuration/net10.0-windows"
$appExe = Join-Path $outputDirectory 'DesktopPet.App.exe'
$dataDirectory = if ($Installed) { Join-Path $env:LOCALAPPDATA 'DesktopPet' }
    else { Join-Path $outputDirectory 'UserData' }
$mode = if ($Installed) { 'Installed' } else { 'Portable' }

$dotnetExe = Join-Path $repoRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetExe -PathType Leaf)) {
    $dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw 'dotnet was not found. Run tools/Install-DotNetSdk.ps1 first.'
    }
    $dotnetExe = $dotnetCommand.Source
}

if ($NoBuild -and -not (Test-Path -LiteralPath $appExe -PathType Leaf)) {
    throw "Build output was not found: $appExe. Retry without -NoBuild."
}

$action = if ($NoBuild) { "Start elfin ($mode)" } else { "Restore, build and start elfin ($mode)" }
if (-not $PSCmdlet.ShouldProcess($appExe, $action)) { return }

# Do not kill an existing instance or build over its loaded DLLs.
# This is a convenience guard, not a cross-process single-instance lock.
$running = @(Get-Process -Name 'DesktopPet.App' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    throw 'DesktopPet.App is already running. Exit it from the tray, then retry.'
}

$previousDotnetRoot = $env:DOTNET_ROOT
$previousDotnetRootX64 = $env:DOTNET_ROOT_X64
Push-Location -LiteralPath $repoRoot
try {
    # The x64 apphost must use the same installation as restore/build.
    $env:DOTNET_ROOT = Split-Path -Parent $dotnetExe
    $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT

    if (-not $NoBuild) {
        & $dotnetExe restore $project --locked-mode --packages (Join-Path $repoRoot '.packages')
        if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed; application was not started.' }

        & $dotnetExe build $project -c $Configuration --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed; application was not started.' }
    }

    if (-not (Test-Path -LiteralPath $appExe -PathType Leaf)) {
        throw "Build output was not found: $appExe."
    }

    $launch = @{
        FilePath = $appExe
        WorkingDirectory = $outputDirectory
        WindowStyle = 'Hidden'
        PassThru = $true
    }
    if (-not $Installed) { $launch.ArgumentList = @('--portable') }
    # Launch the EXE directly to retain its PerMonitorV2 manifest.
    # Hidden suppresses a launcher console; WPF owns showing its interactive windows.
    $process = Start-Process @launch
    Write-Host "elfin process created. PID=$($process.Id); Configuration=$Configuration; Mode=$mode"
    Write-Host "Data: $dataDirectory"
    Write-Host 'Use the tray menu to exit. Process creation does not verify UI readiness.'
}
finally {
    Pop-Location
    $env:DOTNET_ROOT = $previousDotnetRoot
    $env:DOTNET_ROOT_X64 = $previousDotnetRootX64
}
