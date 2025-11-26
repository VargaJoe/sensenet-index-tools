@echo off
echo Building SenseNet Native Index Creation Tool (Experimental)...
echo ========================================================

REM Build the solution
dotnet build ..\sensenet-index-tools.sln --configuration Release

if %ERRORLEVEL% NEQ 0 (
    echo ❌ Build failed!
    exit /b 1
)

echo ✅ Build completed successfully!

REM Check if the native tool was built
if exist "bin\Release\net8.0\sensenet-create-index-native.exe" (
    echo 📁 Native tool executable found at: bin\Release\net8.0\sensenet-create-index-native.exe
    echo.
    echo 🚀 You can now run the experimental tool with:
    echo    .\bin\Release\net8.0\sensenet-create-index-native.exe --help
    echo.
    echo 📖 For usage examples, see README.md
) else (
    echo ⚠️  Native tool executable not found in expected location
    echo    Check the build output for any errors
)

echo.
echo 📊 Build Summary:
echo   - Main tool: sn-index-maintenance-suite
echo   - Native tool: sensenet-create-index-native (Experimental)
echo   - Configuration: Release
echo   - Target Framework: .NET 8.0