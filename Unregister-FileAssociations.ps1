param(
    [string]$ExeName = "LetMeSee.exe"
)

$extensions = ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".cr2", ".cr3", ".nef", ".arw", ".raf", ".orf", ".rw2", ".dng", ".heic", ".heif"
$progId = "LetMeSee.Image"
$appName = "LetMeSee"
$applicationKey = "HKCU:\Software\Classes\Applications\$ExeName"
$registeredApplicationsKey = "HKCU:\Software\RegisteredApplications"
$contextMenuKey = "HKCU:\Software\Classes\SystemFileAssociations\image\shell\LetMeSee"

foreach ($extension in $extensions) {
    $openWithProgIdsKey = "HKCU:\Software\Classes\$extension\OpenWithProgids"
    if (Test-Path -LiteralPath $openWithProgIdsKey) {
        Remove-ItemProperty -LiteralPath $openWithProgIdsKey -Name $progId -ErrorAction SilentlyContinue
    }
}

Remove-Item -LiteralPath "HKCU:\Software\Classes\$progId" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $applicationKey -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $contextMenuKey -Recurse -Force -ErrorAction SilentlyContinue

if (Test-Path -LiteralPath $registeredApplicationsKey) {
    $registeredValue = Get-ItemProperty -LiteralPath $registeredApplicationsKey -Name $appName -ErrorAction SilentlyContinue
    if ($null -ne $registeredValue -and $registeredValue.$appName -eq "Software\Classes\Applications\$ExeName\Capabilities") {
        Remove-ItemProperty -LiteralPath $registeredApplicationsKey -Name $appName -ErrorAction SilentlyContinue
    }
}

if (-not ("ShellChangeNotifier" -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class ShellChangeNotifier
{
    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
"@
}

[ShellChangeNotifier]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)

Write-Host "Removed LetMeSee file association registration for: $($extensions -join ', ')"
Write-Host "Removed image right-click command: 用 LetMeSee 開啟"
