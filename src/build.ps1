$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
& $compiler /nologo /target:exe /out:AssetBuilder.exe /reference:System.Drawing.dll AssetBuilder.cs
if ($LASTEXITCODE -ne 0) { throw 'Asset compiler failed' }
& '.\AssetBuilder.exe' 'ShelfDock.ico'
& $compiler /nologo /target:winexe /platform:anycpu /win32manifest:app.manifest /win32icon:ShelfDock.ico /out:ShelfDock.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll /reference:Microsoft.CSharp.dll ShelfDock.cs StandardUserLauncher.cs StartupRegistration.cs Tests.cs AssemblyInfo.cs
if ($LASTEXITCODE -ne 0) { throw 'App compiler failed' }
Start-Process -FilePath (Join-Path $PSScriptRoot 'ShelfDock.exe') -ArgumentList '--self-test',('"' + (Join-Path $PSScriptRoot 'test-results.txt') + '"') -Wait -WindowStyle Hidden
if (!(Get-Content -LiteralPath 'test-results.txt' -Raw).StartsWith('PASS:')) { throw (Get-Content -LiteralPath 'test-results.txt' -Raw) }
& $compiler /nologo /target:winexe /platform:x64 /win32manifest:app.manifest /win32icon:ShelfDock.ico /out:ShelfDock-Setup.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:Microsoft.CSharp.dll /resource:ShelfDock.exe,ShelfDock.exe Installer.cs Uninstaller.cs StandardUserLauncher.cs StartupRegistration.cs AssemblyInfo.cs
if ($LASTEXITCODE -ne 0) { throw 'Installer compiler failed' }
$fullRemovalTest = Join-Path $PSScriptRoot 'full-uninstall-verification'
Start-Process -FilePath (Join-Path $PSScriptRoot 'ShelfDock-Setup.exe') -ArgumentList '--test-full-uninstall',('"' + $fullRemovalTest + '"') -Wait -WindowStyle Hidden
if (!(Get-Content -LiteralPath (Join-Path $fullRemovalTest 'full-uninstall-test.txt') -Raw).StartsWith('PASS:')) { throw (Get-Content -LiteralPath (Join-Path $fullRemovalTest 'full-uninstall-test.txt') -Raw) }
$handoffTest = Join-Path $PSScriptRoot 'uninstall-handoff-verification'
Start-Process -FilePath (Join-Path $PSScriptRoot 'ShelfDock-Setup.exe') -ArgumentList '--test-uninstall-handoff',('"' + $handoffTest + '"') -Wait -WindowStyle Hidden
if (!(Get-Content -LiteralPath (Join-Path $handoffTest 'handoff-result.txt') -Raw).StartsWith('PASS:')) { throw (Get-Content -LiteralPath (Join-Path $handoffTest 'handoff-result.txt') -Raw) }
Get-Content -LiteralPath 'test-results.txt'
Get-Content -LiteralPath (Join-Path $fullRemovalTest 'full-uninstall-test.txt')
Get-Content -LiteralPath (Join-Path $handoffTest 'handoff-result.txt')
Get-FileHash -LiteralPath 'ShelfDock-Setup.exe'
