param(
    [string]$Token = $(if ($env:GITHUB_TOKEN) { $env:GITHUB_TOKEN } elseif ($env:GH_TOKEN) { $env:GH_TOKEN } else { "" })
)

$ErrorActionPreference = "Stop"

$projectDir = $PSScriptRoot
if ([string]::IsNullOrEmpty($projectDir)) {
    $projectDir = Get-Location
}
$mpvDir = Join-Path $projectDir "..\subprojects\mpv"
$versionFile = Join-Path $mpvDir "version.txt"
$dllExists = Test-Path (Join-Path $mpvDir "libmpv-2.dll")

# Known pinned release fallback in case dynamic metadata fetching fails
$fallbackVersion = "mpv-dev-x86_64-v3-20260911-git-14f2d48cbc.7z"
$fallbackUrl = "https://github.com/zhongfly/mpv-winbuild/releases/download/2026-09-11-14f2d48cbc/mpv-dev-x86_64-v3-20260911-git-14f2d48cbc.7z"

$latestVersion = $null
$downloadUrl = $null

# 1. Fetch releases from GitHub REST API
$apiUrl = "https://api.github.com/repos/zhongfly/mpv-winbuild/releases"
$headers = @{
    "User-Agent" = "Kiriha-Build"
}
if (-not [string]::IsNullOrEmpty($Token)) {
    $headers["Authorization"] = "Bearer $Token"
}

Write-Host "Fetching latest libmpv version info from GitHub API (zhongfly/mpv-winbuild)..."
$releases = $null
try {
    $releases = Invoke-RestMethod -Uri $apiUrl -Headers $headers -TimeoutSec 15
} catch {
    Write-Warning "Direct GitHub REST API call failed: $_"
}

# 2. If REST API failed, try GitHub CLI (`gh api`) if installed
if ($null -eq $releases) {
    $ghCmd = Get-Command gh -ErrorAction SilentlyContinue
    if ($ghCmd) {
        try {
            Write-Host "Attempting metadata retrieval via GitHub CLI (gh)..."
            $ghOutput = & gh api repos/zhongfly/mpv-winbuild/releases --cache 1h 2>$null
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrEmpty($ghOutput)) {
                $releases = $ghOutput | ConvertFrom-Json
            }
        } catch {
            Write-Warning "GitHub CLI call failed: $_"
        }
    }
}

# Extract asset from REST / CLI releases if available
if ($null -ne $releases) {
    foreach ($release in $releases) {
        $asset = $release.assets | Where-Object { $_.name -like "mpv-dev-x86_64-v3-*.7z" } | Select-Object -First 1
        if ($null -ne $asset) {
            $latestVersion = $asset.name
            $downloadUrl = $asset.browser_download_url
            break
        }
    }
    if ($null -eq $latestVersion) {
        # Fallback to standard x86_64 if v3 is not found
        foreach ($release in $releases) {
            $asset = $release.assets | Where-Object { $_.name -like "mpv-dev-x86_64-*.7z" } | Select-Object -First 1
            if ($null -ne $asset) {
                $latestVersion = $asset.name
                $downloadUrl = $asset.browser_download_url
                break
            }
        }
    }
}

# 3. Fallback: Web redirect + expanded_assets scraping (immune to GitHub REST API rate limits)
if ($null -eq $latestVersion -or $null -eq $downloadUrl) {
    Write-Host "Falling back to GitHub Releases web page scraping (bypassing API rate limit)..."
    try {
        $latestReleaseUrl = "https://github.com/zhongfly/mpv-winbuild/releases/latest"
        $effectiveUrl = & curl.exe -sI -L -w "%{url_effective}" -o NUL "$latestReleaseUrl"
        $tag = $effectiveUrl.Trim().Split('/')[-1]
        if (-not [string]::IsNullOrEmpty($tag)) {
            $assetsUrl = "https://github.com/zhongfly/mpv-winbuild/releases/expanded_assets/$tag"
            $htmlLines = & curl.exe -sL "$assetsUrl"
            $html = $htmlLines -join "`n"
            if ($html -match 'href="(?<url>[^"]*(?<ver>mpv-dev-x86_64-v3-[^"]+\.7z))"') {
                $latestVersion = $Matches['ver']
                $downloadUrl = "https://github.com" + $Matches['url']
            } elseif ($html -match 'href="(?<url>[^"]*(?<ver>mpv-dev-x86_64-[^"]+\.7z))"') {
                $latestVersion = $Matches['ver']
                $downloadUrl = "https://github.com" + $Matches['url']
            }
        }
    } catch {
        Write-Warning "Web scraping fallback failed: $_"
    }
}

# 4. Ultimate Fallback: Pinned Release URL
if ($null -eq $latestVersion -or $null -eq $downloadUrl) {
    if ($dllExists) {
        Write-Warning "Could not resolve latest libmpv release, but libmpv-2.dll is already present. Skipping update."
        exit 0
    }
    Write-Warning "Could not resolve latest release dynamically. Falling back to pinned release: $fallbackVersion"
    $latestVersion = $fallbackVersion
    $downloadUrl = $fallbackUrl
}

Write-Host "Target libmpv version: $latestVersion"

# Check if current installed version matches target
$currentVersion = ""
if (Test-Path $versionFile) {
    $currentVersion = (Get-Content $versionFile -Raw).Trim()
}

if ($dllExists -and ($currentVersion -eq $latestVersion)) {
    Write-Host "libmpv is already up to date ($currentVersion)."
    exit 0
}

Write-Host "Updating libmpv from '$currentVersion' to '$latestVersion'..."

# Create temp directory
$tempDir = Join-Path $env:TEMP "mpv_download_$(Get-Random)"
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
$archivePath = Join-Path $tempDir "mpv.7z"

try {
    # Download archive using curl.exe with retry
    Write-Host "Downloading $downloadUrl ..."
    & curl.exe -L -s -S --retry 3 --retry-delay 2 -o "$archivePath" "$downloadUrl"
    if ($LASTEXITCODE -ne 0) {
        throw "curl.exe failed to download the archive with exit code $LASTEXITCODE."
    }

    # Extract archive using built-in Windows tar
    Write-Host "Extracting archive..."
    $extractDir = Join-Path $tempDir "extracted"
    New-Item -ItemType Directory -Path $extractDir -Force | Out-Null
    
    & tar.exe -xf "$archivePath" -C "$extractDir"
    
    # Copy files to target directory
    if (-not (Test-Path $mpvDir)) {
        New-Item -ItemType Directory -Path $mpvDir -Force | Out-Null
    }

    $dllFile = Get-ChildItem -Path $extractDir -Filter "libmpv-2.dll" -Recurse | Select-Object -First 1
    $includeDir = Get-ChildItem -Path $extractDir -Directory -Filter "include" -Recurse | Select-Object -First 1
    $implibFile = Get-ChildItem -Path $extractDir -Filter "libmpv.dll.a" -Recurse | Select-Object -First 1

    if ($null -eq $dllFile) {
        throw "Could not find libmpv-2.dll in the downloaded archive!"
    }

    Write-Host "Copying libmpv-2.dll to $mpvDir..."
    Copy-Item -Path $dllFile.FullName -Destination $mpvDir -Force

    if ($null -ne $includeDir) {
        Write-Host "Copying include folder to $mpvDir..."
        if (Test-Path (Join-Path $mpvDir "include")) {
            Remove-Item -Path (Join-Path $mpvDir "include") -Recurse -Force
        }
        Copy-Item -Path $includeDir.FullName -Destination $mpvDir -Recurse -Force
    }

    if ($null -ne $implibFile) {
        Write-Host "Copying libmpv.dll.a to $mpvDir..."
        Copy-Item -Path $implibFile.FullName -Destination $mpvDir -Force
    }

    # Save version
    Set-Content -Path $versionFile -Value $latestVersion
    Write-Host "libmpv successfully updated to $latestVersion!"
}
finally {
    if (Test-Path $tempDir) {
        Write-Host "Cleaning up temporary files..."
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
