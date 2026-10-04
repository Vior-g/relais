@echo off
rem Recompile Relais.exe avec le compilateur C# fourni avec Windows (.NET Framework 4.x).
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Compilateur introuvable. Installe .NET Framework 4.8 depuis le site de Microsoft.
  pause & exit /b 1
)
cd /d "%~dp0"
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /win32icon:relais.ico /out:..\Relais.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll *.cs
if errorlevel 1 ( echo. & echo ECHEC de la compilation. & pause & exit /b 1 )
echo.
echo OK : Relais.exe a ete regenere dans le dossier parent.
pause
