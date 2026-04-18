@echo off
title AirWitness - Sentinel Dashboard
color 0B
echo.
echo ===================================================
echo     INICIANDO AIRWITNESS SENTINEL DASHBOARD
echo ===================================================
echo.
echo Levantando el servidor local C# y preparando The Ear...
echo (Se abrira tu navegador de forma automatica)
echo.

cd src\Sentinel.Dashboard
dotnet run

pause
