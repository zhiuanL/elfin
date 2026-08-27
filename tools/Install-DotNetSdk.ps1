[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$sdkVersion = (Get-Content -LiteralPath (Join-Path $repoRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$toolsRoot = Join-Path $repoRoot '.tools'
$sdkRoot = Join-Path $toolsRoot 'dotnet'
$sdkExe = Join-Path $sdkRoot 'dotnet.exe'
if (Test-Path -LiteralPath (Join-Path $sdkRoot "sdk/$sdkVersion")) {
    & $sdkExe --version
    exit $LASTEXITCODE
}

function Invoke-DownloadWithBackoff {
    param([string]$Uri, [string]$OutFile)
    $delays = @(1, 3, 7, 15)
    for ($attempt = 0; ; $attempt++) {
        try {
            if ($OutFile) {
                Invoke-WebRequest -Uri $Uri -OutFile $OutFile -TimeoutSec 600
                return
            }
            return Invoke-RestMethod -Uri $Uri -TimeoutSec 30
        }
        catch {
            $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
            $retryable = $status -eq 0 -or $status -eq 408 -or $status -eq 429 -or $status -ge 500
            if ($status -eq 403 -or !$retryable -or $attempt -ge $delays.Count) { throw }
            Write-Warning "Download failed (HTTP $status); retry in $($delays[$attempt]) seconds."
            Start-Sleep -Seconds $delays[$attempt]
        }
    }
}

$metadata = Invoke-DownloadWithBackoff 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$sdk = @($metadata.releases | ForEach-Object { $_.sdks } | Where-Object { $_.version -eq $sdkVersion }) | Select-Object -First 1
$archive = $sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
if (!$archive -or $archive.url -notlike 'https://builds.dotnet.microsoft.com/*') { throw 'Official SDK archive not found.' }
New-Item -ItemType Directory -Path $toolsRoot -Force | Out-Null
$archivePath = Join-Path $toolsRoot "dotnet-sdk-$sdkVersion-win-x64.zip"
Invoke-DownloadWithBackoff -Uri $archive.url -OutFile $archivePath
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA512).Hash -ne $archive.hash) { throw 'SDK SHA-512 mismatch. Archive was not extracted.' }
Expand-Archive -LiteralPath $archivePath -DestinationPath $sdkRoot -Force
& $sdkExe --info
exit $LASTEXITCODE
