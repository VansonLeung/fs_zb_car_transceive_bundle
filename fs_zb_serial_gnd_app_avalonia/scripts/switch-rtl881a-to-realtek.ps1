param(
    [string]$HardwareId = "USB\VID_0BDA&PID_881A",
    [string]$InfPath = "C:\Windows\INF\oem68.inf"
)

$ErrorActionPreference = "Stop"

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
$isElevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isElevated) {
    throw "Run this script from an elevated PowerShell session. It force-switches the device driver back to Realtek."
}

if (-not (Test-Path $InfPath)) {
    throw "INF not found: $InfPath"
}

$signature = @"
using System;
using System.Runtime.InteropServices;

public static class NewDev
{
    [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool UpdateDriverForPlugAndPlayDevices(
        IntPtr hwndParent,
        string hardwareId,
        string fullInfPath,
        uint installFlags,
        out bool rebootRequired);
}
"@

Add-Type -TypeDefinition $signature

$INSTALLFLAG_FORCE = 0x1
$rebootRequired = $false
$ok = [NewDev]::UpdateDriverForPlugAndPlayDevices([IntPtr]::Zero, $HardwareId, $InfPath, $INSTALLFLAG_FORCE, [ref]$rebootRequired)

if (-not $ok) {
    $win32 = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    throw "UpdateDriverForPlugAndPlayDevices failed with Win32 error $win32"
}

Write-Host "Successfully switched $HardwareId back to Realtek using $InfPath"
Write-Host "Reboot required: $rebootRequired"