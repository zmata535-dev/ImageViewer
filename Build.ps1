$ErrorActionPreference = "Stop"

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wpfDir = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF"

$workDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $workDir

$sources = @(
    "NaturalStringComparer.cs",
    "ImageHelper.cs",
    "CropOverlay.cs",
    "PropertiesWindow.cs",
    "MainWindow.cs",
    "Program.cs"
)

$refs = @(
    "/r:`"$wpfDir\PresentationCore.dll`"",
    "/r:`"$wpfDir\PresentationFramework.dll`"",
    "/r:`"$wpfDir\WindowsBase.dll`"",
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll",
    "/r:System.Xaml.dll"
)

$iconPath = Join-Path $workDir "app.ico"
if (-not (Test-Path $iconPath)) {
    $iconPath = "C:\Users\ZM\Downloads\photos.ico"
}
$iconParam = if (Test-Path $iconPath) { "/win32icon:`"$iconPath`"" } else { "" }

$outFile = Join-Path $workDir "ImageViewer.exe"

Write-Host "Compiling Clean Image Viewer with csc.exe..." -ForegroundColor Cyan

$argsList = @(
    "/target:winexe",
    "/optimize+",
    "/platform:anycpu",
    "/out:`"$outFile`""
) + $refs + $sources

if ($iconParam) {
    $argsList += $iconParam
}

& $csc $argsList

if ($LASTEXITCODE -eq 0 -and (Test-Path $outFile)) {
    Write-Host "Build SUCCESS: $outFile" -ForegroundColor Green
} else {
    Write-Error "Build failed with exit code $LASTEXITCODE"
}
