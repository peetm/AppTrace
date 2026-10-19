@echo off
rem Publishes AppTrace as ONE self-contained AppTrace.exe (no .NET install needed on the machine you copy it to).
rem Output goes to the "publish" folder inside this project folder.
set OUT=%~dp0publish
dotnet publish "%~dp0AppTrace.csproj" -c Release -r win-x64 --self-contained true -o "%OUT%" ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none -p:SatelliteResourceLanguages=en
if errorlevel 1 exit /b 1
echo.
echo Published: %OUT%AppTrace.exe
dir "%OUT%AppTrace.exe" | find "AppTrace.exe"
