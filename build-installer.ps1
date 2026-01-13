# AutoPilot Agent Build & Installer Script
# Run this script to create a distributable installer

param(
    [string]$Configuration = "Release",
    [switch]$SkipBuild,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
$PublishDir = Join-Path $ProjectRoot "publish"
$DistDir = Join-Path $ProjectRoot "dist"
$UIProject = Join-Path $ProjectRoot "src\AutoPilotAgent.UI\AutoPilotAgent.UI.csproj"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  AutoPilot Agent Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Clean previous builds
if (Test-Path $PublishDir) {
    Write-Host "Cleaning publish directory..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $PublishDir
}

if (-not (Test-Path $DistDir)) {
    New-Item -ItemType Directory -Path $DistDir | Out-Null
}

if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "Step 1: Building and Publishing..." -ForegroundColor Green
    Write-Host "-----------------------------------"
    
    # Publish as self-contained single file
    dotnet publish $UIProject `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o $PublishDir
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed!" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "Build successful!" -ForegroundColor Green
    
    # Show output size
    $exeFile = Get-ChildItem -Path $PublishDir -Filter "*.exe" | Select-Object -First 1
    if ($exeFile) {
        $sizeMB = [math]::Round($exeFile.Length / 1MB, 2)
        Write-Host "Output: $($exeFile.Name) ($sizeMB MB)" -ForegroundColor Cyan
    }
}

if (-not $SkipInstaller) {
    Write-Host ""
    Write-Host "Step 2: Creating Installer..." -ForegroundColor Green
    Write-Host "-----------------------------"
    
    # Check if Inno Setup is installed
    $innoPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $innoPath)) {
        $innoPath = "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    }
    
    if (Test-Path $innoPath) {
        $issFile = Join-Path $ProjectRoot "installer\setup.iss"
        & $innoPath $issFile
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Installer created successfully!" -ForegroundColor Green
            $installer = Get-ChildItem -Path $DistDir -Filter "*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if ($installer) {
                Write-Host "Output: $($installer.FullName)" -ForegroundColor Cyan
            }
        } else {
            Write-Host "Installer creation failed!" -ForegroundColor Red
        }
    } else {
        Write-Host "Inno Setup not found. Skipping installer creation." -ForegroundColor Yellow
        Write-Host "Download from: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Standalone executable available at:" -ForegroundColor Cyan
        Write-Host "  $PublishDir\AutoPilotAgent.exe" -ForegroundColor White
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build Complete!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Distribution options:" -ForegroundColor White
Write-Host "  1. Standalone EXE: $PublishDir\AutoPilotAgent.exe" -ForegroundColor Gray
Write-Host "  2. Installer: $DistDir\AutoPilotAgent-Setup-*.exe" -ForegroundColor Gray
