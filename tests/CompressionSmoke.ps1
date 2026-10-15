param([string]$Fixture = (Join-Path $PSScriptRoot '..\screens\testschx.mlt'))
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$toolsDirectory = Join-Path $projectRoot 'aSMP\bin\Debug'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$testExe = Join-Path $toolsDirectory 'CompressionSmoke.exe'
& $compiler /nologo /target:exe /r:System.Windows.Forms.dll /r:System.Drawing.dll "/r:$toolsDirectory\aSMP.exe" "/out:$testExe" (Join-Path $PSScriptRoot 'CompressionSmoke.cs') (Join-Path $PSScriptRoot 'LoaderMachine.cs') (Join-Path $projectRoot 'aSMP\Zx0Package.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $testExe (Resolve-Path -LiteralPath $Fixture).Path
if ($LASTEXITCODE -ne 0) { throw 'Compression smoke test failed.' }
