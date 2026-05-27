param()

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$targetDir = Join-Path $repoRoot "native\ffmpeg\win-x64\bin"
$requiredPatterns = @(
    "avcodec-*.dll",
    "avformat-*.dll",
    "avutil-*.dll",
    "swscale-*.dll"
)

$hasRuntime = $true
foreach ($pattern in $requiredPatterns) {
    if (-not (Get-ChildItem -Path $targetDir -Filter $pattern -ErrorAction SilentlyContinue | Select-Object -First 1)) {
        $hasRuntime = $false
        break
    }
}

if ($hasRuntime) {
    Write-Host "FFmpeg runtime already available at $targetDir"
    return
}

$downloadUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("fs_zb_ffmpeg_" + [System.Guid]::NewGuid().ToString("N"))
$archivePath = Join-Path $tempRoot "ffmpeg-win64-shared.zip"
$extractPath = Join-Path $tempRoot "extract"

New-Item -ItemType Directory -Path $tempRoot | Out-Null
New-Item -ItemType Directory -Path $extractPath | Out-Null
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

try {
    Write-Host "Downloading FFmpeg shared runtime from $downloadUrl"
    Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath

    Write-Host "Extracting FFmpeg shared runtime..."
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractPath -Force

    $sourceBinDir = Get-ChildItem -Path $extractPath -Directory -Recurse |
        Where-Object {
            (Test-Path (Join-Path $_.FullName "avcodec-*.dll")) -or
            (Get-ChildItem -Path $_.FullName -Filter "avcodec-*.dll" -ErrorAction SilentlyContinue | Select-Object -First 1)
        } |
        Select-Object -First 1

    if ($null -eq $sourceBinDir) {
        throw "Could not locate the extracted FFmpeg bin directory."
    }

    Write-Host "Copying FFmpeg DLLs into $targetDir"
    Get-ChildItem -Path $sourceBinDir.FullName -Filter "*.dll" | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $targetDir $_.Name) -Force
    }

    Write-Host "FFmpeg runtime is ready."
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}