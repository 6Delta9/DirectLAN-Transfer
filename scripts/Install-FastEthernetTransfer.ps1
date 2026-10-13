# Fast Ethernet File Transfer - One-Click Windows Installer Script
param(
    [switch]$Uninstall = $false
)

$appName = "Fast Ethernet File Transfer"
$exeName = "FastEthernetTransfer.exe"
$iconName = "newappicon.ico"
$installDir = "$env:LOCALAPPDATA\Programs\FastEthernetTransfer"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   Fast Ethernet File Transfer v2.0 - Setup Script       " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if ($Uninstall) {
    Write-Host "Uninstalling $appName..." -ForegroundColor Yellow
    
    # Remove firewall rule
    try {
        netsh advfirewall firewall delete rule name="$appName" | Out-Null
        Write-Host "✓ Windows Firewall rule removed." -ForegroundColor Green
    } catch { }

    # Remove shortcuts
    $desktopShortcut = [System.IO.Path]::Combine([Environment]::GetFolderPath("Desktop"), "$appName.lnk")
    $startMenuShortcut = [System.IO.Path]::Combine([Environment]::GetFolderPath("Programs"), "$appName.lnk")
    Remove-Item $desktopShortcut -ErrorAction SilentlyContinue
    Remove-Item $startMenuShortcut -ErrorAction SilentlyContinue

    # Remove directory
    if (Test-Path $installDir) {
        Remove-Item $installDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "✓ Application files removed." -ForegroundColor Green
    }
    Write-Host "Uninstallation complete. (User settings and logs were preserved in %LOCALAPPDATA%\FastEthernetTransfer)" -ForegroundColor Green
    exit 0
}

Write-Host "Installing $appName to $installDir..." -ForegroundColor Yellow

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceExe = Join-Path $scriptRoot "..\publish\win-x64\$exeName"
$sourceIcon = Join-Path $scriptRoot "..\newappicon.ico"

if (-not (Test-Path $sourceExe)) {
    if (Test-Path (Join-Path $PSScriptRoot $exeName)) {
        $sourceExe = Join-Path $PSScriptRoot $exeName
    } else {
        Write-Host "Building application executable..." -ForegroundColor Cyan
        dotnet publish (Join-Path $scriptRoot "..\FastEthernetTransfer.Wpf\FastEthernetTransfer.Wpf.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o (Join-Path $scriptRoot "..\publish\win-x64")
    }
}

# Create installation folder
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

# Copy binary and icon
$targetExe = Join-Path $installDir $exeName
Copy-Item -Path $sourceExe -Destination $targetExe -Force
Write-Host "✓ Copied binary to $targetExe" -ForegroundColor Green

if (Test-Path $sourceIcon) {
    Copy-Item -Path $sourceIcon -Destination (Join-Path $installDir $iconName) -Force
}

# Create Shortcuts with proper IconLocation
try {
    $wshShell = New-Object -ComObject WScript.Shell
    
    # Desktop shortcut
    $desktopPath = [Environment]::GetFolderPath("Desktop")
    $shortcut = $wshShell.CreateShortcut((Join-Path $desktopPath "$appName.lnk"))
    $shortcut.TargetPath = $targetExe
    $shortcut.IconLocation = "$targetExe,0"
    $shortcut.Description = "High-Speed PC-to-PC Ethernet File Transfer"
    $shortcut.WorkingDirectory = $installDir
    $shortcut.Save()
    Write-Host "✓ Created Desktop Shortcut with application icon" -ForegroundColor Green

    # Start menu shortcut
    $programsPath = [Environment]::GetFolderPath("Programs")
    $startShortcut = $wshShell.CreateShortcut((Join-Path $programsPath "$appName.lnk"))
    $startShortcut.TargetPath = $targetExe
    $startShortcut.IconLocation = "$targetExe,0"
    $startShortcut.Description = "High-Speed PC-to-PC Ethernet File Transfer"
    $startShortcut.WorkingDirectory = $installDir
    $startShortcut.Save()
    Write-Host "✓ Created Start Menu Shortcut with application icon" -ForegroundColor Green
} catch {
    Write-Host "Warning: Could not create shortcuts: $($_.Exception.Message)" -ForegroundColor Yellow
}

# Configure Windows Firewall rule
Write-Host "Configuring Windows Firewall exception..." -ForegroundColor Yellow
try {
    # Delete old rule if present to avoid duplication
    netsh advfirewall firewall delete rule name="$appName" | Out-Null
    # Add inbound rule
    netsh advfirewall firewall add rule name="$appName" dir=in action=allow program="$targetExe" enable=yes profile=any | Out-Null
    Write-Host "✓ Windows Firewall rule configured successfully!" -ForegroundColor Green
} catch {
    Write-Host "Note: Could not automatically set firewall rule. Run PowerShell as Administrator if needed." -ForegroundColor Yellow
}

Write-Host "`nInstallation successful! Launching $appName..." -ForegroundColor Cyan
Start-Process -FilePath $targetExe
