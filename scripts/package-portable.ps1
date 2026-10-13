# Package Portable ZIP Distribution for Fast Ethernet File Transfer v2.0
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionRoot = Resolve-Path "$scriptRoot\.."
$publishDir = "$solutionRoot\publish\win-x64"
$exePath = "$publishDir\FastEthernetTransfer.exe"
$iconPath = "$solutionRoot\newappicon.ico"
$distDir = "$solutionRoot\dist"
$zipOutput = "$distDir\FastEthernetTransfer-Portable-win-x64.zip"
$stageRoot = "$solutionRoot\publish\staging"
$stageDir = "$stageRoot\FastEthernetTransfer-Portable"

if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Packaging Portable ZIP Release: $zipOutput" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Ensure published binary exists
if (-not (Test-Path $exePath)) {
    Write-Host "Published binary not found. Running publish-win-x64.ps1..." -ForegroundColor Yellow
    & "$scriptRoot\publish-win-x64.ps1"
}

# Clean and create staging directory
if (Test-Path $stageRoot) {
    Remove-Item $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

# 1. Copy executable
Copy-Item -Path $exePath -Destination $stageDir -Force
Write-Host "[OK] Added FastEthernetTransfer.exe" -ForegroundColor Green

# 2. Copy icon
if (Test-Path $iconPath) {
    Copy-Item -Path $iconPath -Destination $stageDir -Force
    Write-Host "[OK] Added newappicon.ico" -ForegroundColor Green
}

# 3. Create portable README.txt
$readmeLines = @(
"================================================================================",
"FAST ETHERNET FILE TRANSFER v2.0 - PORTABLE EDITION (Windows x64)",
"================================================================================",
"",
"DESCRIPTION:",
"Fast Ethernet Transfer is a standalone, high-speed PC-to-PC file transfer and",
"Game Updater application designed for Windows 10/11.",
"",
"NO INSTALLATION OR RUNTIME REQUIRED:",
"This portable release is completely self-contained. It includes the required",
"runtime and dependencies. No .NET SDK or runtime installation is necessary.",
"",
"HOW TO RUN:",
"1. Extract the entire ZIP archive to any folder (e.g., Desktop, USB, C:\Tools).",
"2. Double-click 'FastEthernetTransfer.exe' to launch the application.",
"3. Open the application on both PCs connected to the same Ethernet network.",
"4. The computers will discover each other automatically with zero configuration.",
"",
"FEATURES:",
"- High-Speed TCP Streaming for individual files and whole folders.",
"- Game Updater: Differential game installation updater (transfers only new and",
"  changed files, verified via SHA-256).",
"- Zero-Config LAN peer discovery via UDP multicast/broadcast.",
"- Real-time speed and ETA transfer dashboard.",
"- Resume support for interrupted transfers.",
"",
"NETWORK & FIREWALL:",
"When launched for the first time, Windows Defender Firewall may prompt to allow",
"network access. Click 'Allow access' on Private networks.",
"You can also run the included 'Configure-Firewall.cmd' (as Administrator) to",
"automatically add the inbound firewall rule.",
"",
"DATA STORAGE:",
"All configuration settings and logs are safely stored in your user profile:",
"- Settings: %LOCALAPPDATA%\FastEthernetTransfer\settings.json",
"- Logs:     %LOCALAPPDATA%\FastEthernetTransfer\Logs\",
"",
"The portable folder remains clean and read-only friendly.",
"================================================================================"
)

[System.IO.File]::WriteAllLines((Join-Path $stageDir "README.txt"), $readmeLines, [System.Text.Encoding]::UTF8)
Write-Host "[OK] Added README.txt" -ForegroundColor Green

# 4. Create optional Configure-Firewall.cmd helper
$cmdLines = @(
"@echo off",
"echo Configuring Windows Firewall rule for Fast Ethernet Transfer...",
"netsh advfirewall firewall add rule name=""Fast Ethernet File Transfer"" dir=in action=allow program=""%~dp0FastEthernetTransfer.exe"" enable=yes profile=any",
"echo Done! You can now close this window.",
"pause"
)
[System.IO.File]::WriteAllLines((Join-Path $stageDir "Configure-Firewall.cmd"), $cmdLines, [System.Text.Encoding]::ASCII)
Write-Host "[OK] Added Configure-Firewall.cmd" -ForegroundColor Green

# 5. Create ZIP archive
if (Test-Path $zipOutput) {
    Remove-Item -Path $zipOutput -Force
}

Write-Host "Compressing archive to $zipOutput..." -ForegroundColor Yellow
Compress-Archive -Path "$stageDir" -DestinationPath $zipOutput -CompressionLevel Optimal

# Clean staging directory
Remove-Item -Path $stageRoot -Recurse -Force -ErrorAction SilentlyContinue

# Verify ZIP file exists and has size
if (Test-Path $zipOutput) {
    $zipSizeMb = [math]::Round((Get-Item $zipOutput).Length / 1MB, 2)
    Write-Host "[SUCCESS] Portable package created: $zipOutput ($zipSizeMb MB)" -ForegroundColor Green
} else {
    Write-Error "Failed to create ZIP package."
    exit 1
}
