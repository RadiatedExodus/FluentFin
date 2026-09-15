param(
    [string]$Rid = "win-x64",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Resolve-Path (Join-Path $scriptRoot "..")
$versionsPath = Join-Path $projectRoot "native/mpv/versions.json"

if (-not (Test-Path -LiteralPath $versionsPath)) {
    throw "Unable to find mpv version metadata at '$versionsPath'."
}

$versions = Get-Content -LiteralPath $versionsPath -Raw | ConvertFrom-Json
$entry = $versions.mpv.$Rid

if ($null -eq $entry) {
    throw "No libmpv download is configured for RID '$Rid'."
}

$runtimeDir = Join-Path $projectRoot "native/mpv/$Rid"
$dllPath = Join-Path $runtimeDir "libmpv-2.dll"

if ((Test-Path -LiteralPath $dllPath) -and -not $Force) {
    Write-Host "libmpv already exists at '$dllPath'. Use -Force to redownload."
    exit 0
}

$workDir = Join-Path ([System.IO.Path]::GetTempPath()) ("FluentFin-libmpv-" + [System.Guid]::NewGuid())
$archivePath = Join-Path $workDir $entry.archive
$extractDir = Join-Path $workDir "extract"

New-Item -ItemType Directory -Force -Path $workDir, $extractDir, $runtimeDir | Out-Null

try {
    Write-Host "Downloading libmpv $Rid from $($entry.url)"
    Invoke-WebRequest -Uri $entry.url -OutFile $archivePath

    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash.ToLowerInvariant()
    $expectedHash = [string]$entry.sha256

    if ($actualHash -ne $expectedHash.ToLowerInvariant()) {
        throw "SHA-256 mismatch for '$archivePath'. Expected $expectedHash, got $actualHash."
    }

    $sevenZip = Get-Command 7z -ErrorAction SilentlyContinue
    if ($null -eq $sevenZip) {
        $sevenZip = Get-Command 7za -ErrorAction SilentlyContinue
    }

    if ($null -ne $sevenZip) {
        & $sevenZip.Source x $archivePath "-o$extractDir" -y | Write-Verbose
        if ($LASTEXITCODE -ne 0) {
            throw "7-Zip failed to extract '$archivePath'."
        }
    }
    else {
        & tar -xf $archivePath -C $extractDir
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to extract '$archivePath'. Install 7-Zip and make sure '7z' is on PATH, then rerun this script."
        }
    }

    $dll = Get-ChildItem -LiteralPath $extractDir -Recurse -Filter "libmpv-2.dll" | Select-Object -First 1
    if ($null -eq $dll) {
        throw "The archive did not contain libmpv-2.dll."
    }

    Copy-Item -LiteralPath $dll.FullName -Destination $dllPath -Force

    $licenseFiles = Get-ChildItem -LiteralPath $extractDir -Recurse -File |
        Where-Object { $_.Name -match '^(LICENSE|COPYING|NOTICE)(\..*)?$' }

    foreach ($file in $licenseFiles) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $runtimeDir $file.Name) -Force
    }

    Write-Host "libmpv installed at '$dllPath'."
}
finally {
    Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue
}
