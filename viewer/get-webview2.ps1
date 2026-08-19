$ErrorActionPreference = 'Stop'
$idx = Invoke-RestMethod 'https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/index.json'
$stable = $idx.versions | Where-Object { $_ -notmatch '-' } | Select-Object -Last 1
Write-Output ("Version: " + $stable)
$dir = 'C:\Users\phil\Documents\DBD Overlay\viewer\webview2-sdk'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$nupkg = Join-Path $dir 'webview2.zip'
Invoke-WebRequest ('https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/' + $stable + '/microsoft.web.webview2.' + $stable + '.nupkg') -OutFile $nupkg
Expand-Archive -Path $nupkg -DestinationPath (Join-Path $dir 'pkg') -Force
Get-ChildItem -Recurse (Join-Path $dir 'pkg') -Include *.dll | Select-Object FullName, Length
