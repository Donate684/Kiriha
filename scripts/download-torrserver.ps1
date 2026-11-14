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

# Known pinned MatriX release fallback in case dynamic metadata fetching fails.
# IMPORTANT: Only MatriX.* tags are compatible with Kiriha's CLI flags (-p, -d, -i) and HTTP API (/echo, /torrents, /settings).
# Do NOT use /releases/latest blindly — YouROK/TorrServer also publishes incompatible Silo.* releases in the same repository.
$fallbackVersion = "MatriX.150"
$fallbackUrl = "https://github.com/YouROK/TorrServer/releases/download/MatriX.150/TorrServer-windows-amd64.exe"

$currentVersion = ""
if (Test-Path $versionFile) {
    $currentVersion = (Get-Content $versionFile -Raw).Trim()
}
$hasValidLocalMatriX = $exeExists -and ($currentVersion -match '^MatriX\.')

$latestVersion = $null
$downloadUrl = $null

function Select-LatestMatriXRelease($releases) {
    if ($null -eq $releases) { return $null }
    foreach ($rel in $releases) {
        if ($rel.draft -or $rel.prerelease) { continue }
        if ($rel.tag_name -notmatch '^MatriX\.') { continue }
        $asset = $rel.assets | Where-Object { $_.name -eq "TorrServer-windows-amd64.exe" } | Select-Object -First 1
        if ($null -ne $asset -and -not [string]::IsNullOrEmpty($asset.browser_download_url)) {
            return [PSCustomObject]@{
                Version = [string]$rel.tag_name
                Url     = [string]$asset.browser_download_url
            }
        }
    }
    return $null
}

# 1. Fetch recent releases from GitHub REST API and filter strictly for MatriX.*
$apiUrl = "https://api.github.com/repos/YouROK/TorrServer/releases?per_page=30"
$headers = @{
    "User-Agent" = "Kiriha-Build"
}
if (-not [string]::IsNullOrEmpty($Token)) {
    $headers["Authorization"] = "Bearer $Token"
}

Write-Host "Fetching TorrServer MatriX release list from GitHub API (YouROK/TorrServer)..."
try {
    $releases = Invoke-RestMethod -Uri $apiUrl -Headers $headers -TimeoutSec 15
    $selected = Select-LatestMatriXRelease $releases
    if ($null -ne $selected) {
        $latestVersion = $selected.Version
        $downloadUrl = $selected.Url
    }
} catch {
    Write-Warning "Direct GitHub REST API call failed: $_"
}

# 2. If REST API failed, try GitHub CLI (`gh api`) if installed
if ($null -eq $latestVersion -or $null -eq $downloadUrl) {
    $ghCmd = Get-Command gh -ErrorAction SilentlyContinue
    if ($ghCmd) {
        try {
            Write-Host "Attempting metadata retrieval via GitHub CLI (gh)..."
            $ghOutput = & gh api "repos/YouROK/TorrServer/releases?per_page=30" --cache 1h 2>$null
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrEmpty($ghOutput)) {
                $releases = $ghOutput | ConvertFrom-Json
                $selected = Select-LatestMatriXRelease $releases
                if ($null -ne $selected) {
                    $latestVersion = $selected.Version
                    $downloadUrl = $selected.Url
                }
            }
        } catch {
            Write-Warning "GitHub CLI call failed: $_"
        }
    }
}

# 3. Fallback: GitHub Releases Atom feed (bypasses REST API rate limits while filtering strictly for MatriX.*)
if ($null -eq $latestVersion -or $null -eq $downloadUrl) {
    Write-Host "Falling back to GitHub Releases Atom feed..."
    try {
        $atomUrl = "https://github.com/YouROK/TorrServer/releases.atom"
        $atomXml = & curl.exe -sL --max-time 15 "$atomUrl"
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrEmpty($atomXml)) {
            $match = [regex]::Match($atomXml, 'releases/tag/(MatriX\.[0-9A-Za-z._-]+)')
            if ($match.Success) {
                $tag = $match.Groups[1].Value
                $latestVersion = $tag
                $downloadUrl = "https://github.com/YouROK/TorrServer/releases/download/$tag/TorrServer-windows-amd64.exe"
            }
        }
    } catch {
        Write-Warning "Atom feed fallback failed: $_"
    }
}

# 4. Ultimate Fallback: Pinned MatriX Release URL
if ($null -eq $latestVersion -or $null -eq $downloadUrl) {
    if ($hasValidLocalMatriX) {
        Write-Warning "Could not resolve latest TorrServer MatriX release, but valid $currentVersion is already present. Skipping update."
        exit 0
    }
    Write-Warning "Could not resolve latest MatriX release dynamically. Falling back to pinned release: $fallbackVersion"
    $latestVersion = $fallbackVersion
    $downloadUrl = $fallbackUrl
}

Write-Host "Target TorrServer version: $latestVersion"

if ($hasValidLocalMatriX -and ($currentVersion -eq $latestVersion)) {
    Write-Host "TorrServer is already up to date ($currentVersion)."
    exit 0
}

Write-Host "Updating TorrServer from '$currentVersion' to '$latestVersion'..."

function Download-AndValidateMatriX([string]$url, [string]$destFile) {
    Write-Host "Downloading $url ..."
    & curl.exe -L -f -s -S --retry 3 --retry-delay 2 -o "$destFile" "$url"
    if ($LASTEXITCODE -ne 0) {
        throw "curl.exe failed to download TorrServer with exit code $LASTEXITCODE."
    }

    $downloadedSize = (Get-Item $destFile).Length
    if ($downloadedSize -lt (15 * 1024 * 1024)) {
        throw "Downloaded file is suspiciously small ($downloadedSize bytes). Download likely failed."
    }

    # Verify CLI compatibility (--version must succeed and report MatriX)
    $versionOut = (& $destFile --version 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $versionOut -notmatch 'MatriX') {
        throw "Downloaded TorrServer binary failed MatriX CLI check (exit=$LASTEXITCODE, output='$versionOut')."
    }
    Write-Host "Verified binary: $versionOut"
}

$tempDir = Join-Path $env:TEMP "torrserver_download_$(Get-Random)"
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
$tempFile = Join-Path $tempDir "torrserver.exe"

try {
    try {
        Download-AndValidateMatriX -url $downloadUrl -destFile $tempFile
    } catch {
        if ($downloadUrl -ne $fallbackUrl) {
            Write-Warning "Primary MatriX download failed ($_). Falling back to pinned $fallbackVersion..."
            $latestVersion = $fallbackVersion
            Download-AndValidateMatriX -url $fallbackUrl -destFile $tempFile
        } else {
            throw
        }
    }

    if (-not (Test-Path $torrServerDir)) {
        New-Item -ItemType Directory -Path $torrServerDir -Force | Out-Null
    }

    Write-Host "Placing torrserver.exe into $torrServerDir..."
    Copy-Item -Path $tempFile -Destination $exePath -Force

    Set-Content -Path $versionFile -Value $latestVersion -NoNewline
    Write-Host "TorrServer successfully updated to $latestVersion!"
}
finally {
    if (Test-Path $tempDir) {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
