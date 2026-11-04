@echo off
rem Recompile la derniere version puis lance Dofus Retro Manager en administrateur
rem (necessaire pour lire le trafic reseau)
cd /d "%~dp0"

tasklist /FI "IMAGENAME eq DofusRetroManager.exe" | find /I "DofusRetroManager.exe" >nul
if not errorlevel 1 (
    echo Dofus Retro Manager est deja ouvert : fermez-le, puis relancez ce script.
    pause
    exit /b 1
)

rem Projets temporaires laisses par une compilation WPF interrompue : ils bloquent dotnet publish
del /q "DofusRetroManager\*_wpftmp.csproj" 2>nul

echo Compilation de la derniere version...
dotnet publish "DofusRetroManager\DofusRetroManager.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish -nologo -v q
if errorlevel 1 (
    echo Echec de la compilation : voir les erreurs ci-dessus.
    pause
    exit /b 1
)

powershell -NoProfile -Command "Start-Process -FilePath '%~dp0publish\DofusRetroManager.exe' -Verb RunAs"
