@echo off
rem Recompile la derniere version puis lance Dofus Bank Manager en administrateur
rem (necessaire pour lire le trafic reseau)
cd /d "%~dp0"

tasklist /FI "IMAGENAME eq DofusBankManager.exe" | find /I "DofusBankManager.exe" >nul
if not errorlevel 1 (
    echo Dofus Bank Manager est deja ouvert : fermez-le, puis relancez ce script.
    pause
    exit /b 1
)

rem Projets temporaires laisses par une compilation WPF interrompue : ils bloquent dotnet publish
del /q "DofusBankManager\*_wpftmp.csproj" 2>nul

echo Compilation de la derniere version...
dotnet publish "DofusBankManager\DofusBankManager.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish -nologo -v q
if errorlevel 1 (
    echo Echec de la compilation : voir les erreurs ci-dessus.
    pause
    exit /b 1
)

powershell -NoProfile -Command "Start-Process -FilePath '%~dp0publish\DofusBankManager.exe' -Verb RunAs"
