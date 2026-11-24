@echo off
rem Compila e instala ProContaVF en la carpeta de producción, guardando antes la versión anterior (docs/plataforma/entrega.md).
rem   instalar.cmd                 instala en C:\Docs\ProConta
rem   instalar.cmd "<carpeta>"     instala en otra carpeta
cd /d "%~dp0"
if "%~1"=="" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0instalar.ps1"
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0instalar.ps1" -Destino "%~1"
)
exit /b %errorlevel%
