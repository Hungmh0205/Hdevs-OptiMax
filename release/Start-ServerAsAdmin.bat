@echo off
title Launching OPTIMAX Server as Administrator...
echo ==================================================
echo   REQUESTING ADMINISTRATOR ELEVATION FOR OPTIMAX
echo ==================================================
powershell -Command "Start-Process node -ArgumentList 'server.js' -WorkingDirectory '%~dp0' -Verb RunAs"
