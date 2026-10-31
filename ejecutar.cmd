@echo off
rem Abre ProContaVF compilado con compilar.cmd (docs/plataforma/contenedor.md).
rem   ejecutar.cmd                              usa la BD de la configuración del puesto (la pide la primera vez)
rem   ejecutar.cmd --bd "C:\ruta\de\la\BD.accdb"   usa esa BD sin cambiar la configuración
cd /d "%~dp0"
if not exist "src\ProConta.App\bin\Release\net10.0-windows\ProContaVF.exe" call compilar.cmd
start "" "src\ProConta.App\bin\Release\net10.0-windows\ProContaVF.exe" %*
