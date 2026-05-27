param(
    [string]$VidPid = "0BDA:881A",
    [int]$Channel = 64,
    [string]$Width = "20",
    [string]$Codec = "AUTO",
    [string]$KeyPath = "gs.key"
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$workspaceRoot = Split-Path -Parent $repoRoot
$ffmpegRuntimeScript = Join-Path $scriptRoot "download-ffmpeg-win-x64.ps1"
$nativeBuildScript = Join-Path $repoRoot "native\fpv4win_bridge\scripts\build-windows.ps1"
$desktopProject = Join-Path $repoRoot "FsZbGroundApp.Desktop\FsZbGroundApp.Desktop.csproj"
$desktopOutput = Join-Path ([System.IO.Path]::GetTempPath()) ("fs_zb_ground_app_autolaunch_" + [System.Guid]::NewGuid().ToString("N"))
$desktopDll = Join-Path $desktopOutput "FsZbGroundApp.Desktop.dll"
$desktopOutDirMsbuild = $desktopOutput + [System.IO.Path]::DirectorySeparatorChar

function Resolve-KeyPath {
    param([string]$InputPath)

    if ([string]::IsNullOrWhiteSpace($InputPath)) {
        return $InputPath
    }

    if ([System.IO.Path]::IsPathRooted($InputPath)) {
        if (Test-Path $InputPath) {
            return (Resolve-Path $InputPath).Path
        }

        return $InputPath
    }

    $candidates = @(
        (Join-Path $repoRoot $InputPath),
        (Join-Path $workspaceRoot $InputPath),
        (Join-Path $workspaceRoot "__references\fpv4win-main\fpv4win-main\$InputPath")
    )

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path $candidate) {
            return (Resolve-Path $candidate).Path
        }
    }

    return $InputPath
}

$resolvedKeyPath = Resolve-KeyPath -InputPath $KeyPath

Push-Location $repoRoot
try {
    Write-Host "Ensuring FFmpeg runtime DLLs are available..."
    & $ffmpegRuntimeScript
    if (-not $?) {
        throw "FFmpeg runtime setup failed."
    }

    Write-Host "Building Release native bridge and refreshing desktop output..."
    & $nativeBuildScript -Configuration Release -CopyToDirectories $desktopOutput
    if (-not $?) {
        throw "native bridge build failed."
    }

    Write-Host "Building Release desktop app..."
    dotnet build $desktopProject -c Release -p:OutDir=$desktopOutDirMsbuild
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE"
    }

    if (-not (Test-Path $desktopDll)) {
        throw "Desktop output not found: $desktopDll"
    }

    Write-Host "Launching ground app in auto-listen mode. Start the air side after the window appears."
    Push-Location $desktopOutput
    try {
        & dotnet $desktopDll --auto-start-wfb --wfb-vidpid $VidPid --wfb-channel $Channel --wfb-width $Width --wfb-codec $Codec --wfb-key $resolvedKeyPath --console-log
    }
    finally {
        Pop-Location
    }
}
finally {
    Pop-Location
}