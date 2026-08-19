@echo off
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /out:DbdClockViewer.exe /lib:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xaml.dll /r:WindowsBase.dll /r:PresentationCore.dll /r:PresentationFramework.dll DbdClockViewer.cs
if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
echo Built DbdClockViewer.exe
endlocal
