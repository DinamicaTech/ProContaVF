@echo off
rem Compila la solución completa en Release x64 (docs/plataforma/entrega.md).
cd /d "%~dp0"
dotnet build ProContaVF.slnx -c Release -p:Platform=x64
exit /b %errorlevel%
