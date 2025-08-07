@echo off
REM Navigate to your project directory (adjust path if needed)
cd /d "%~dp0"

REM Clean the project (optional)
dotnet clean

REM Restore dependencies
dotnet restore

REM Build the project
dotnet build

REM Run the project
dotnet run

pause