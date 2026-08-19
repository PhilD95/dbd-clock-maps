@echo off
setlocal
cd /d "%~dp0"
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
copy /y "webview2-sdk\pkg\lib\net462\Microsoft.Web.WebView2.Core.dll" . >nul
copy /y "webview2-sdk\pkg\lib\net462\Microsoft.Web.WebView2.WinForms.dll" . >nul
copy /y "webview2-sdk\pkg\runtimes\win-x64\native\WebView2Loader.dll" . >nul
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /out:DbdClockWeb.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Microsoft.Web.WebView2.Core.dll /r:Microsoft.Web.WebView2.WinForms.dll DbdClockWeb.cs
if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
echo Built DbdClockWeb.exe
endlocal
