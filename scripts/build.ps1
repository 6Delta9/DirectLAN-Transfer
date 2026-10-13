# Master Build & Package Script for Fast Ethernet File Transfer v2.0
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionRoot = Resolve-Path "$scriptRoot\.."

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Fast Ethernet File Transfer v2.0 - Master Build & Release" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Run Tests
Write-Host "`n1. Running automated unit and integration tests..." -ForegroundColor Yellow
dotnet test "$solutionRoot\FastEthernetTransfer.Tests\FastEthernetTransfer.Tests.csproj" -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Error "Automated tests failed! Aborting release."
    exit $LASTEXITCODE
}
Write-Host "[SUCCESS] All automated tests passed successfully!" -ForegroundColor Green

# 2. Publish Self-Contained Single-File Executable
Write-Host "`n2. Publishing self-contained win-x64 single-file executable..." -ForegroundColor Yellow
& "$scriptRoot\publish-win-x64.ps1"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish step failed!"
    exit $LASTEXITCODE
}

# 3. Package Portable ZIP Distribution
Write-Host "`n3. Packaging Portable ZIP distribution..." -ForegroundColor Yellow
& "$scriptRoot\package-portable.ps1"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Portable packaging step failed!"
    exit $LASTEXITCODE
}

# 4. Check for Inno Setup compiler & build installer
Write-Host "`n4. Checking Windows Installer compiler..." -ForegroundColor Yellow
& "$scriptRoot\build-installer.ps1"

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " Build and packaging completed successfully!" -ForegroundColor Green
Write-Host " Artifacts created:" -ForegroundColor Green
Write-Host "  1. Standalone EXE: $solutionRoot\publish\win-x64\FastEthernetTransfer.exe" -ForegroundColor Green
Write-Host "  2. Portable ZIP:   $solutionRoot\dist\FastEthernetTransfer-Portable-win-x64.zip" -ForegroundColor Green
Write-Host "  3. Setup EXE:      $solutionRoot\dist\FastEthernetTransfer-Setup-win-x64.exe" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
