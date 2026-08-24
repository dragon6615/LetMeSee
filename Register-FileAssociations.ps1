param(
    [string]$ExePath = (Join-Path $PSScriptRoot "bin\Release\net9.0-windows\win-x64\publish\LetMeSee.exe")
)

$resolvedExe = Resolve-Path -LiteralPath $ExePath -ErrorAction Stop
$resolvedExe = $resolvedExe.Path
$command = "`"$resolvedExe`" `"%1`""
$extensions = ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".cr2", ".cr3", ".nef", ".arw", ".raf", ".orf", ".rw2", ".dng", ".heic", ".heif"
$progId = "LetMeSee.Image"
$appName = "LetMeSee"
$appExeName = Split-Path -Leaf $resolvedExe
$applicationKey = "HKCU:\Software\Classes\Applications\$appExeName"
$capabilitiesKey = "$applicationKey\Capabilities"
$contextMenuKey = "HKCU:\Software\Classes\SystemFileAssociations\image\shell\LetMeSee"
$emptyBinary = [byte[]]@()

New-Item -Path "HKCU:\Software\Classes\$progId\shell\open\command" -Force | Out-Null
Set-Item -Path "HKCU:\Software\Classes\$progId" -Value "LetMeSee Image"
Set-ItemProperty -Path "HKCU:\Software\Classes\$progId" -Name "FriendlyTypeName" -Value "LetMeSee Image"
Set-ItemProperty -Path "HKCU:\Software\Classes\$progId\DefaultIcon" -Name "(default)" -Value $resolvedExe -ErrorAction SilentlyContinue
New-Item -Path "HKCU:\Software\Classes\$progId\DefaultIcon" -Force | Out-Null
Set-Item -Path "HKCU:\Software\Classes\$progId\DefaultIcon" -Value $resolvedExe
Set-Item -Path "HKCU:\Software\Classes\$progId\shell\open\command" -Value $command

New-Item -Path "$applicationKey\shell\open\command" -Force | Out-Null
New-Item -Path "$applicationKey\SupportedTypes" -Force | Out-Null
Set-ItemProperty -Path $applicationKey -Name "FriendlyAppName" -Value $appName
Set-Item -Path "$applicationKey\shell\open\command" -Value $command

New-Item -Path "$capabilitiesKey\FileAssociations" -Force | Out-Null
Set-ItemProperty -Path $capabilitiesKey -Name "ApplicationName" -Value $appName
Set-ItemProperty -Path $capabilitiesKey -Name "ApplicationDescription" -Value "Lightweight image viewer"
New-Item -Path "HKCU:\Software\RegisteredApplications" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\RegisteredApplications" -Name $appName -Value "Software\Classes\Applications\$appExeName\Capabilities"

New-Item -Path "$contextMenuKey\command" -Force | Out-Null
Set-ItemProperty -Path $contextMenuKey -Name "MUIVerb" -Value "用 LetMeSee 開啟"
Set-ItemProperty -Path $contextMenuKey -Name "Icon" -Value $resolvedExe
Set-Item -Path "$contextMenuKey\command" -Value $command

foreach ($extension in $extensions) {
    New-Item -Path "HKCU:\Software\Classes\$extension" -Force | Out-Null
    New-Item -Path "HKCU:\Software\Classes\$extension\OpenWithProgids" -Force | Out-Null
    New-ItemProperty -Path "HKCU:\Software\Classes\$extension\OpenWithProgids" -Name $progId -PropertyType Binary -Value $emptyBinary -Force | Out-Null
    New-ItemProperty -Path "$applicationKey\SupportedTypes" -Name $extension -PropertyType String -Value "" -Force | Out-Null
    Set-ItemProperty -Path "$capabilitiesKey\FileAssociations" -Name $extension -Value $progId
}

Write-Host "Registered LetMeSee for Open With: $($extensions -join ', ')"
Write-Host "Added image right-click command: 用 LetMeSee 開啟"
