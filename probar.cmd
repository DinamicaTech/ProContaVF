@echo off
rem Compila y ejecuta las pruebas automáticas (docs/plataforma/entrega.md).
rem   probar.cmd          todas las pruebas, también las que usan una copia de la BD Access
rem   probar.cmd sinbd    solo las pruebas que no tocan Access (las que ejecuta cada hilo antes de su PR)
cd /d "%~dp0"
set FILTRO=
if /i "%~1"=="sinbd" set FILTRO=--filter "Categoria!=Access"
dotnet build ProContaVF.slnx -c Release -p:Platform=x64
if errorlevel 1 exit /b %errorlevel%
dotnet test ProContaVF.slnx -c Release -p:Platform=x64 --no-build %FILTRO%
exit /b %errorlevel%
