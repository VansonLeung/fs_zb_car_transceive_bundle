param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$CopyToDesktopOutputs,
    [string[]]$CopyToDirectories
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = (Resolve-Path (Join-Path $scriptDir "..")).Path
$repoRoot = (Resolve-Path (Join-Path $projectRoot "..\..\..")).Path
$buildRoot = if ($env:FPV4WIN_BRIDGE_BUILD_ROOT) { $env:FPV4WIN_BRIDGE_BUILD_ROOT } else { "C:\b\fpv4win_bridge" }
$buildDir = Join-Path $buildRoot $Configuration
$vcpkgRoot = if ($env:FPV4WIN_BRIDGE_VCPKG_ROOT) { $env:FPV4WIN_BRIDGE_VCPKG_ROOT } else { "C:\vcpkg-fsbridge" }
$rtlRoot = Join-Path $repoRoot "__references\fpv4win-main\fpv4win-main\3rd\rtl8812au-monitor-pcap"
$devourerRoot = Join-Path $repoRoot "__references\fpv4win-main\fpv4win-main\3rd\devourer"

function Invoke-External {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$StepName
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$StepName failed with exit code $LASTEXITCODE"
    }
}

function Resolve-CMakeExe {
    $cmakeCommand = Get-Command cmake -ErrorAction SilentlyContinue
    if ($cmakeCommand) {
        return $cmakeCommand.Source
    }

    $fallback = "C:\Program Files\CMake\bin\cmake.exe"
    if (Test-Path $fallback) {
        return $fallback
    }

    throw "CMake executable was not found. Install CMake first."
}

function Ensure-Git {
    $gitCommand = Get-Command git -ErrorAction SilentlyContinue
    if (-not $gitCommand) {
        throw "Git executable was not found in PATH."
    }
}

function Ensure-Repo {
    param(
        [string]$Path,
        [string]$Url
    )

    if ((Test-Path $Path) -and ((Get-ChildItem -Force $Path | Measure-Object).Count -eq 0)) {
        Remove-Item -Recurse -Force $Path
    }

    if (-not (Test-Path $Path)) {
        Invoke-External -FilePath "git" -Arguments @("clone", $Url, $Path) -StepName "git clone $Url"
    }
}

function Ensure-Vcpkg {
    if (-not (Test-Path $vcpkgRoot)) {
        Invoke-External -FilePath "git" -Arguments @("clone", "https://github.com/microsoft/vcpkg", $vcpkgRoot) -StepName "git clone vcpkg" | Out-Null
    }

    $vcpkgExe = Join-Path $vcpkgRoot "vcpkg.exe"
    if (-not (Test-Path $vcpkgExe)) {
        $bootstrapScript = Join-Path $vcpkgRoot "bootstrap-vcpkg.bat"
        Invoke-External -FilePath $bootstrapScript -Arguments @("-disableMetrics") -StepName "vcpkg bootstrap" | Out-Null
    }

    return $vcpkgExe
}

function Resolve-BuiltDll {
    param([string]$BuildPath)

    $candidates = @(
        (Join-Path $BuildPath ("$Configuration\fpv4win_bridge.dll")),
        (Join-Path $BuildPath "fpv4win_bridge.dll")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "Could not locate fpv4win_bridge.dll after build."
}

function Resolve-DependencyDll {
    param(
        [string]$FileName,
        [string]$BuildConfiguration
    )

    $binCandidates = @()
    if ($BuildConfiguration -eq "Debug") {
        $binCandidates += (Join-Path $vcpkgRoot "installed\x64-windows\debug\bin\$FileName")
    }

    $binCandidates += (Join-Path $vcpkgRoot "installed\x64-windows\bin\$FileName")

    foreach ($candidate in $binCandidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "Could not locate dependency DLL '$FileName' under $vcpkgRoot."
}

function Copy-BuildOutputs {
    param(
        [string]$BuiltDll,
        [string[]]$DependencyDlls,
        [string[]]$TargetDirectories
    )

    foreach ($target in $TargetDirectories | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) {
        if (-not (Test-Path $target)) {
            New-Item -ItemType Directory -Path $target -Force | Out-Null
        }

        Copy-Item -Force $BuiltDll (Join-Path $target "fpv4win_bridge.dll")
        foreach ($dependencyDll in $DependencyDlls) {
            Copy-Item -Force $dependencyDll (Join-Path $target (Split-Path -Leaf $dependencyDll))
        }

        Write-Host "Copied fpv4win_bridge.dll to $target"
        Write-Host "Copied dependency DLLs to $target"
    }
}

Ensure-Git
$cmakeExe = Resolve-CMakeExe

Ensure-Repo -Path $rtlRoot -Url "https://github.com/TalusL/rtl8812au-monitor-pcap.git"
Ensure-Repo -Path $devourerRoot -Url "https://github.com/OpenIPC/devourer.git"

$vcpkgExe = Ensure-Vcpkg
Invoke-External -FilePath $vcpkgExe -Arguments @("install", "libusb:x64-windows", "libsodium:x64-windows") -StepName "vcpkg install"

$toolchainFile = Join-Path $vcpkgRoot "scripts\buildsystems\vcpkg.cmake"
if (-not (Test-Path $toolchainFile)) {
    throw "vcpkg toolchain file not found: $toolchainFile"
}

$configureArgs = @(
    "-S", $projectRoot,
    "-B", $buildDir,
    "-A", "x64",
    "-DCMAKE_TOOLCHAIN_FILE=$toolchainFile",
    "-DVCPKG_TARGET_TRIPLET=x64-windows"
)
Invoke-External -FilePath $cmakeExe -Arguments $configureArgs -StepName "cmake configure"

$buildArgs = @("--build", $buildDir, "--config", $Configuration)
Invoke-External -FilePath $cmakeExe -Arguments $buildArgs -StepName "cmake build"

$builtDll = Resolve-BuiltDll -BuildPath $buildDir
Write-Host "Built bridge DLL: $builtDll"

if ($CopyToDesktopOutputs -or ($CopyToDirectories | Measure-Object).Count -gt 0) {
    $dependencyDlls = @(
        (Resolve-DependencyDll -FileName "libusb-1.0.dll" -BuildConfiguration $Configuration),
        (Resolve-DependencyDll -FileName "libsodium.dll" -BuildConfiguration $Configuration)
    )

    $targets = @()
    if ($CopyToDesktopOutputs) {
        $targets += @(
            (Join-Path $repoRoot "fs_zb_serial_gnd_app_avalonia\FsZbGroundApp.Desktop\bin\Debug\net9.0"),
            (Join-Path $repoRoot "fs_zb_serial_gnd_app_avalonia\FsZbGroundApp.Desktop\bin\Release\net9.0")
        )
    }

    if (($CopyToDirectories | Measure-Object).Count -gt 0) {
        $targets += $CopyToDirectories
    }

    Copy-BuildOutputs -BuiltDll $builtDll -DependencyDlls $dependencyDlls -TargetDirectories ($targets | Select-Object -Unique)
}
