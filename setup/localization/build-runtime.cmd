@echo off
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars32.bat" >nul
if errorlevel 1 exit /b 1
cd /d "%~dp0"
if not exist vendor\minhook-1.3.4\src\buffer.c powershell -NoProfile -Command "Expand-Archive -LiteralPath '%~dp0vendor-MinHook-1.3.4.zip' -DestinationPath '%~dp0vendor'"
if not exist vendor\minhook-1.3.4\src\buffer.c exit /b 1
rc /nologo /c65001 /fo GameLanguage.res GameLanguage.rc
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /O2 /MT /EHsc /LD /utf-8 /DUNICODE /D_UNICODE GameLanguage.cpp vendor\minhook-1.3.4\src\buffer.c vendor\minhook-1.3.4\src\hook.c vendor\minhook-1.3.4\src\trampoline.c vendor\minhook-1.3.4\src\hde\hde32.c GameLanguage.res /link /out:a12_live_score.dll user32.lib gdi32.lib shlwapi.lib
