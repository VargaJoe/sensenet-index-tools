# Build script for SenseNet Native Index Creation Tool (Experimental)
Write-Host "Building SenseNet Native Index Creation Tool (Experimental)..." -ForegroundColor Cyan
Write-Host "=" * 60 -ForegroundColor Cyan

# Build the solution
Write-Host "Building solution..." -ForegroundColor Yellow
$buildResult = dotnet build ..\sensenet-index-tools.sln --configuration Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Build completed successfully!" -ForegroundColor Green

# Check if the native tool was built
$nativeToolPath = "bin\Release\net8.0\sensenet-create-index-native.exe"
if (Test-Path $nativeToolPath) {
    Write-Host "📁 Native tool executable found at: $nativeToolPath" -ForegroundColor Green
    Write-Host ""
    Write-Host "🚀 You can now run the experimental tool with:" -ForegroundColor Green
    Write-Host "   .\bin\Release\net8.0\sensenet-create-index-native.exe --help" -ForegroundColor White
    Write-Host ""
    Write-Host "📖 For usage examples, see README.md" -ForegroundColor Green
} else {
    Write-Host "⚠️  Native tool executable not found in expected location" -ForegroundColor Yellow
    Write-Host "   Check the build output for any errors" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "📊 Build Summary:" -ForegroundColor Cyan
Write-Host "  - Main tool: sn-index-maintenance-suite" -ForegroundColor White
Write-Host "  - Native tool: sensenet-create-index-native (Experimental)" -ForegroundColor White
Write-Host "  - Configuration: Release" -ForegroundColor White
Write-Host "  - Target Framework: .NET 8.0" -ForegroundColor White
Write-Host ""
Write-Host "🔬 Ready for experimentation!" -ForegroundColor Green