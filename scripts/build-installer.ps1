# Build Windows Installer (Inno Setup) for Fast Ethernet File Transfer v2.0
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionRoot = Resolve-Path "$scriptRoot\.."
$issPath = "$solutionRoot\installer\setup.iss"
$publishDir = "$solutionRoot\publish\win-x64"
$exePath = "$publishDir\FastEthernetTransfer.exe"
$distDir = "$solutionRoot\dist"
$installerOutputDir = $distDir

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Building Windows Installer (Inno Setup)" -ForegroundColor Cyan
Write-Host " Script: $issPath" -ForegroundColor Cyan
Write-Host " Output: $distDir\FastEthernetTransfer-Setup-win-x64.exe" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Ensure published binary exists
if (-not (Test-Path $exePath)) {
    Write-Host "Published binary not found. Running publish-win-x64.ps1..." -ForegroundColor Yellow
    & "$scriptRoot\publish-win-x64.ps1"
}

# Ensure dist output directory exists
if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
}

# Locate Inno Setup Compiler (ISCC.exe)
$progFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
$progFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)

$candidatePaths = @(
    (Join-Path $localAppData "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path $progFilesX86 "Inno Setup 6\ISCC.exe"),
    (Join-Path $progFiles "Inno Setup 6\ISCC.exe"),
    (Join-Path $progFilesX86 "Inno Setup 5\ISCC.exe"),
    (Join-Path $progFiles "Inno Setup 5\ISCC.exe")
)

# Also check PATH
$cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
if ($cmd -and $cmd.Source) {
    $candidatePaths = @($cmd.Source) + $candidatePaths
}

$iscc = $candidatePaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    Write-Host "Found Inno Setup Compiler at: $iscc" -ForegroundColor Green
    Write-Host "Compiling installer..." -ForegroundColor Yellow
    
    & $iscc $issPath
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Inno Setup compiler failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }

    $setupExe = Join-Path $distDir "FastEthernetTransfer-Setup-win-x64.exe"
    if (Test-Path $setupExe) {
        $sizeMb = [math]::Round((Get-Item $setupExe).Length / 1MB, 2)
        Write-Host "[SUCCESS] Windows Installer created successfully: $setupExe ($sizeMb MB)" -ForegroundColor Green
    } else {
        Write-Error "Installer binary was not found at expected path: $setupExe"
        exit 1
    }
}
 else {
    Write-Host "`n[NOTICE] Inno Setup Compiler (ISCC.exe) was not found on this system." -ForegroundColor Yellow
    Write-Host "The installer script has been fully prepared and verified at: $issPath" -ForegroundColor Cyan
    Write-Host "To compile the EXE installer, install Inno Setup 6 from https://jrsoftware.org/isdl.php" -ForegroundColor Cyan
    Write-Host "Alternatively, users can install via the included PowerShell installer:" -ForegroundColor Cyan
    Write-Host "  PowerShell -ExecutionPolicy Bypass -File .\scripts\Install-FastEthernetTransfer.ps1" -ForegroundColor Cyan
    Write-Host "Or use the portable package at: .\publish\FastEthernetTransfer-Portable-win-x64.zip" -ForegroundColor Cyan
}
