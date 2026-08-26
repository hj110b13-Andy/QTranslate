@echo off
setlocal
cd /d "%~dp0"

echo.
echo   QTranslate - restore original Google service file
echo   ================================================
echo.

set "QT=%QTRANSLATE_DIR%"
if defined QT goto :haveqt
set "QT=C:\Program Files (x86)\QTranslate"
if exist "%QT%\QTranslate.exe" goto :haveqt
set "QT=C:\Program Files\QTranslate"
:haveqt
if not exist "%QT%\QTranslate.exe" goto :notfound
echo   Install folder : %QT%

set "DST=%QT%\Services\Google Translate"
if not exist "%DST%\Service.js.original" goto :noback

set "PROBE=%DST%\qtfix-write-probe.tmp"
del "%PROBE%" >nul 2>&1
break > "%PROBE%" 2>nul
if not exist "%PROBE%" goto :noaccess
del "%PROBE%" >nul 2>&1

set "WASRUNNING="
tasklist /FI "IMAGENAME eq QTranslate.exe" 2>nul | find /I "QTranslate.exe" >nul
if errorlevel 1 goto :notrunning
set "WASRUNNING=1"
taskkill /IM QTranslate.exe /F >nul 2>&1
echo   Stopped running QTranslate
:notrunning

copy /y "%DST%\Service.js.original" "%DST%\Service.js" >nul
if errorlevel 1 goto :copyfail
echo   Restored the original Service.js
echo.
echo   DONE - back to the stock file.
echo   Note: the stock file uses a Google endpoint that no longer works.
echo.

if not defined WASRUNNING goto :done
explorer.exe "%QT%\QTranslate.exe"
echo   QTranslate restarted.
goto :done

:noaccess
echo   ERROR: cannot write to
echo          %DST%
echo          Right-click this file and choose "Run as administrator".
goto :done

:notfound
echo   ERROR: QTranslate installation folder not found.
goto :done

:noback
echo   ERROR: no backup found at
echo          %DST%\Service.js.original
echo          Nothing to restore.
goto :done

:copyfail
echo   ERROR: could not copy the file back.
goto :done

:done
echo.
if /i not "%~1"=="/nopause" pause
