@echo off
setlocal
cd /d "%~dp0"

echo.
echo   QTranslate - Google translate service fix
echo   ========================================
echo.

rem ---- Locate the QTranslate installation ----
rem Set QTRANSLATE_DIR beforehand to point at a portable/non-default install.
set "QT=%QTRANSLATE_DIR%"
if defined QT goto :haveqt
set "QT=C:\Program Files (x86)\QTranslate"
if exist "%QT%\QTranslate.exe" goto :haveqt
set "QT=C:\Program Files\QTranslate"
:haveqt
if not exist "%QT%\QTranslate.exe" goto :notfound
echo   Install folder : %QT%

set "DST=%QT%\Services\Google Translate"
if not exist "%DST%\Service.js" goto :nosvc
if not exist "patch\Google Translate\Service.js" goto :nopatch

rem ---- Confirm the target folder is actually writable ----
rem A real write probe beats checking for admin rights: a portable install
rem living under the user profile needs no elevation at all.
set "PROBE=%DST%\qtfix-write-probe.tmp"
del "%PROBE%" >nul 2>&1
break > "%PROBE%" 2>nul
if not exist "%PROBE%" goto :noaccess
del "%PROBE%" >nul 2>&1

rem ---- Stop QTranslate if it is running ----
set "WASRUNNING="
tasklist /FI "IMAGENAME eq QTranslate.exe" 2>nul | find /I "QTranslate.exe" >nul
if errorlevel 1 goto :notrunning
set "WASRUNNING=1"
taskkill /IM QTranslate.exe /F >nul 2>&1
echo   Stopped running QTranslate
:notrunning

rem ---- Back up the pristine original, once ----
if exist "%DST%\Service.js.original" goto :haveback
copy /y "%DST%\Service.js" "%DST%\Service.js.original" >nul
if errorlevel 1 goto :copyfail
echo   Backed up original as Service.js.original
goto :applied
:haveback
echo   Backup already exists, kept as is
:applied

copy /y "patch\Google Translate\Service.js" "%DST%\Service.js" >nul
if errorlevel 1 goto :copyfail
echo   Installed patched Service.js
echo.
echo   DONE - fix applied successfully.
echo.

if not defined WASRUNNING goto :nostart
rem Launch through explorer so QTranslate does not inherit admin rights
explorer.exe "%QT%\QTranslate.exe"
echo   QTranslate restarted.
goto :done
:nostart
echo   You can start QTranslate now.
goto :done

:noaccess
echo   ERROR: cannot write to
echo          %DST%
echo.
echo          Right-click Fix-QTranslate.cmd and choose
echo          "Run as administrator".
goto :done

:notfound
echo   ERROR: QTranslate installation folder not found.
echo          Set QTRANSLATE_DIR to the install path and retry.
goto :done

:nosvc
echo   ERROR: original service file not found:
echo          %DST%\Service.js
goto :done

:nopatch
echo   ERROR: patch file not found:
echo          %~dp0patch\Google Translate\Service.js
goto :done

:copyfail
echo   ERROR: could not copy files.
echo          You can restore from Service.js.original
goto :done

:done
echo.
if /i not "%~1"=="/nopause" pause
