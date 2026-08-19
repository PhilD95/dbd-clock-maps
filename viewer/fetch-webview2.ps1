$ErrorActionPreference = 'Stop'
$index = Invoke-RestMethod 'https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/index.json'
$stable = $index.versions | Where-Object { $_ -notmatch '-' }
$ver = $stable[-1]
Write-Host ('Using WebView2 SDK ' + $ver)
$tmp = Join-Path $env:TEMP ('webview2_' + $ver)
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$nupkg = Join-Path $tmp 'pkg.zip'
Invoke-WebRequest ('https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/' + $ver + '/microsoft.web.webview2.' + $ver + '.nupkg') -OutFile $nupkg
$x = Join-Path $tmp 'x'
Expand-Archive -Path $nupkg -DestinationPath $x -Force
function Find-One($root, $name, $prefer) {
  $c = Get-ChildItem -Path $root -Recurse -Filter $name
  $p = $c | Where-Object { $_.FullName -match $prefer } | Select-Object -First 1
  if (-not $p) { $p = $c | Select-Object -First 1 }
  return $p.FullName
}
$dst = $PSScriptRoot
Copy-Item (Find-One $x 'Microsoft.Web.WebView2.Core.dll' 'net4') $dst -Force
Copy-Item (Find-One $x 'Microsoft.Web.WebView2.WinForms.dll' 'net4') $dst -Force
Copy-Item (Find-One $x 'WebView2Loader.dll' 'win-x64') $dst -Force
Write-Host 'WebView2 SDK files copied.'
