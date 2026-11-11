$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
& $compiler /nologo /target:exe /out:AssetBuilder.exe /reference:System.Drawing.dll AssetBuilder.cs
if ($LASTEXITCODE -ne 0) { throw 'Asset compiler failed' }
& '.\AssetBuilder.exe' 'ShelfDock.ico'
& $compiler /nologo /target:winexe /platform:anycpu /win32manifest:app.manifest /win32icon:ShelfDock.ico /out:ShelfDock.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll /reference:Microsoft.CSharp.dll ShelfDock.cs StartupRegistration.cs Tests.cs AssemblyInfo.cs
if ($LASTEXITCODE -ne 0) { throw 'App compiler failed' }
Start-Process -FilePath (Join-Path $PSScriptRoot 'ShelfDock.exe') -ArgumentList '--self-test',('"' + (Join-Path $PSScriptRoot 'test-results.txt') + '"') -Wait -WindowStyle Hidden
if (!(Get-Content -LiteralPath 'test-results.txt' -Raw).StartsWith('PASS:')) { throw (Get-Content -LiteralPath 'test-results.txt' -Raw) }
& $compiler /nologo /target:winexe /win32manifest:app.manifest /win32icon:ShelfDock.ico /out:ShelfDock-Setup.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:Microsoft.CSharp.dll /resource:ShelfDock.exe,ShelfDock.exe Installer.cs StartupRegistration.cs AssemblyInfo.cs
if ($LASTEXITCODE -ne 0) { throw 'Installer compiler failed' }
Get-Content -LiteralPath 'test-results.txt'
Get-FileHash -LiteralPath 'ShelfDock-Setup.exe'
