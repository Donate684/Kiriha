param(
    [string]$Token = $(if ($env:GITHUB_TOKEN) { $env:GITHUB_TOKEN } elseif ($env:GH_TOKEN) { $env:GH_TOKEN } else { "" })
)

$ErrorActionPreference = "Stop"

$projectDir = $PSScriptRoot
if ([string]::IsNullOrEmpty($projectDir)) {
    $projectDir = Get-Location
}
$torrServerDir = Join-Path $projectDir "..\subprojects\torrserver"
$versionFile = Join-Path $torrServerDir "version.txt"
$exePath = Join-Path $torrServerDir "torrserver.exe"
$exeExists = Test-Path $exePath

# Known pinned release fallback in case dynamic metadata fetching fails
$fallbackVersion = "MatriX.146"
$fallbackUrl = "https://github.com/YouROK/TorrServer/releases/download/MatriX.146/TorrServer-windows-amd64.exe"

$latestVersion = $null
$downloadUrl = $null

# 1. Fetch latest release from GitHub REST API
$apiUrl = "https://api.github.com/repos/YouROK/TorrServer/releases/latest"
$headers = @{
    "User-Agent" = "Kiriha-Build"
}
if (-not [string]::IsNullOrEmpty($Token)) {
    $headers["Authorization"] = "Bearer $Token"
}

Write-Host "Fetching latest TorrServer version info from GitHub API (YouROK/TorrServer)..."
$release = $null
try {
    $release = Invoke-RestMethod -Uri $apiUrl -Headers $headers -TimeoutSec 15
} catch {
    Write-Warning "Direct GitHub REST API call failed: $_"
}

# 2. If REST API failed, try GitHub CLI (`gh api`) if installed
if ($null -eq $release) {
    $ghCmd = Get-Command gh -ErrorAction SilentlyContinue
    if ($ghCmd) {
        try {
            Write-Host "Attempting metadata retrieval via GitHub CLI (gh)..."
            $ghOutput = & gh api repos/YouROK/TorrServer/releases/latest --cache 1h 2>$null
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrEmpty($ghOutput)) {
                $release = $ghOutput | ConvertFrom-Json
            }
        } catch {
            Write-Warning "GitHub CLI call failed: $_"
        }
    }
}

# Extract asset from release (specifically TorrServer-windows-amd64.exe, excluding -gst- build)
if ($null -ne $release) {
    $asset = $release.assets | Where-Object { $_.name -eq "TorrServer-windows-amd64.exe" } | Select-Object -First 1
    if ($null -ne $asset) {
        $latestVersion = $release.tag_name
        $downloadUrl = $asset.browser_download_url
    }
}

# 3. Fallback: Web redirect inspection (bypassing API rate limit)
if ($null -eq $latestVersion -or $null -eq $downloadUrl) {
    Write-Host "Falling back to GitHub Releases web page scraping..."
    try {
        $latestReleaseUrl = "https://github.com/YouROK/TorrServer/releases/latest"
        $effectiveUrl = & curl.exe -sI -L -w "%{url_effective}" -o NUL "$latestReleaseUrl"
        $tag = $effectiveUrl.Trim().Split('/')[-1]
        if (-not [string]::IsNullOrEmpty($tag)) {
            $latestVersion = $tag
            $downloadUrl = "https://github.com/YouROK/TorrServer/releases/download/$tag/TorrServer-windows-amd64.exe"
        }
    } catch {
        Write-Warning "Web scraping fallback failed: $_"
    }
}

# 4. Ultimate Fallback: Pinned Release URL
if ($null -eq $latestVersion -or $null -eq $downloadUrl) {
    if ($exeExists) {
        Write-Warning "Could not resolve latest TorrServer release, but torrserver.exe is already present. Skipping update."
        exit 0
    }
    Write-Warning "Could not resolve latest release dynamically. Falling back to pinned release: $fallbackVersion"
    $latestVersion = $fallbackVersion
    $downloadUrl = $fallbackUrl
}

Write-Host "Target TorrServer version: $latestVersion"

# Check if current installed version matches target
$currentVersion = ""
if (Test-Path $versionFile) {
    $currentVersion = (Get-Content $versionFile -Raw).Trim()
}

if ($exeExists -and ($currentVersion -eq $latestVersion)) {
    Write-Host "TorrServer is already up to date ($currentVersion)."
    exit 0
}

Write-Host "Updating TorrServer from '$currentVersion' to '$latestVersion'..."

# Create temp directory
$tempDir = Join-Path $env:TEMP "torrserver_download_$(Get-Random)"
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
$tempFile = Join-Path $tempDir "torrserver.exe"

try {
    # Download binary using curl.exe with retry
    Write-Host "Downloading $downloadUrl ..."
    & curl.exe -L -s -S --retry 3 --retry-delay 2 -o "$tempFile" "$downloadUrl"
    if ($LASTEXITCODE -ne 0) {
        throw "curl.exe failed to download TorrServer with exit code $LASTEXITCODE."
    }

    # Validate file size (must be at least 15MB)
    $downloadedSize = (Get-Item $tempFile).Length
    if ($downloadedSize -lt (15 * 1024 * 1024)) {
        throw "Downloaded file is suspiciously small ($downloadedSize bytes). Download likely failed."
    }

    if (-not (Test-Path $torrServerDir)) {
        New-Item -ItemType Directory -Path $torrServerDir -Force | Out-Null
    }

    Write-Host "Placing torrserver.exe into $torrServerDir..."
    Copy-Item -Path $tempFile -Destination $exePath -Force

    # Save version
    Set-Content -Path $versionFile -Value $latestVersion
    Write-Host "TorrServer successfully updated to $latestVersion!"
}
finally {
    if (Test-Path $tempDir) {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
