# Publish Windows x64 Self-Contained Single-File Executable
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionRoot = Resolve-Path "$scriptRoot\.."
$projectPath = "$solutionRoot\FastEthernetTransfer.Wpf\FastEthernetTransfer.Wpf.csproj"
$publishDir = "$solutionRoot\publish\win-x64"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Publishing Self-Contained Win-x64 Executable" -ForegroundColor Cyan
Write-Host " Target Output: $publishDir\FastEthernetTransfer.exe" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Ensure any running instances are closed before building
Get-Process -Name "FastEthernetTransfer" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

# Clean stale publish directory if safe
if (Test-Path $publishDir) {
    Get-ChildItem -Path $publishDir -File | Remove-Item -Force -ErrorAction SilentlyContinue
}

# Publish command
dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish command failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

$exePath = "$publishDir\FastEthernetTransfer.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "Executable not found at expected path: $exePath"
    exit 1
}

$fileSizeMb = [math]::Round((Get-Item $exePath).Length / 1MB, 2)
Write-Host "[SUCCESS] Self-contained executable published: $exePath ($fileSizeMb MB)" -ForegroundColor Green

# Quick verification: test launching executable
Write-Host "Verifying executable startup..." -ForegroundColor Yellow
$testProcess = Start-Process -FilePath $exePath -WorkingDirectory $publishDir -PassThru
Start-Sleep -Milliseconds 1200
$isRunning = Get-Process -Id $testProcess.Id -ErrorAction SilentlyContinue

if ($isRunning) {
    Stop-Process -Id $testProcess.Id -Force
    $testProcess.WaitForExit(3000)
    Write-Host "[VERIFIED] Executable launched and initialized successfully!" -ForegroundColor Green
} else {
    Write-Error "Executable exited prematurely with code $($testProcess.ExitCode)!"
    exit 1
}
