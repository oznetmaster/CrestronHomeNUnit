# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE file in the project root for full license information.

param([string] $IlRepack = 'ilrepack')
$ErrorActionPreference = 'Stop'
$outputDirectory = Join-Path $PSScriptRoot 'bin'
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$library = Join-Path $outputDirectory 'Library.dll'
$original = Join-Path $outputDirectory 'Original.exe'
$merged = Join-Path $outputDirectory 'Merged.exe'
& $compiler /nologo /target:library "/out:$library" (Join-Path $PSScriptRoot 'Library.cs')
if ($LASTEXITCODE -ne 0) { throw 'Library compilation failed.' }
& $compiler /nologo "/out:$original" "/reference:$library" (Join-Path $PSScriptRoot 'Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'Program compilation failed.' }
Write-Host 'Original:'
& $original
if ($LASTEXITCODE -ne 0) { throw 'The original assembly failed the baseline.' }
& $IlRepack "/out:$merged" $original $library
if ($LASTEXITCODE -ne 0) { throw 'ILRepack failed.' }
Write-Host 'Merged:'
& $merged
Write-Host "Merged result exit code: $LASTEXITCODE (expected 0; 1 demonstrates the defect)."
