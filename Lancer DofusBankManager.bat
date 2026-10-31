@echo off
rem Lance Dofus Bank Manager en administrateur (necessaire pour lire le trafic reseau)
powershell -NoProfile -Command "Start-Process -FilePath '%~dp0publish\DofusBankManager.exe' -Verb RunAs"
