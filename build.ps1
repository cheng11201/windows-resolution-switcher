$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$release = Join-Path $PSScriptRoot 'release'
New-Item -ItemType Directory -Force -Path $release | Out-Null
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /utf8output /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/out:$release\ResolutionSwitcher.exe" "$PSScriptRoot\src\ResolutionSwitcher.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Get-Item -LiteralPath "$release\ResolutionSwitcher.exe" | Select-Object FullName,Length
