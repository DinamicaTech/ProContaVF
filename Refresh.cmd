@echo off
rem Brings the latest changes of the repository (docs, open work list) into this folder.
rem Double-click it, then press F5 in the page.
cd /d "%~dp0"
git pull --ff-only
pause
