# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE file in the project root for full license information.

param(
    [ValidateSet('Debug','Release')][string] $Configuration = 'Debug',
    [switch] $SkipBuild
)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) {
    & dotnet build (Join-Path $PSScriptRoot 'CrestronHomeNUnit.sln') -c $Configuration -p:DeployAfterBuild=false
    if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
}
$driverOutput = Join-Path $PSScriptRoot "CrestronHomeNUnit.Driver\bin\$Configuration\net472"
$validator = Join-Path $PSScriptRoot "CrestronHomeNUnit.DesktopValidation\bin\$Configuration\net472\CrestronHomeNUnit.DesktopValidation.exe"
$packagePath = Join-Path $driverOutput 'CrestronHomeNUnit.Driver.pkg'
$resultRoot = Join-Path $PSScriptRoot ('artifacts\validation\' + $Configuration + '\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
$extractedPath = Join-Path $resultRoot 'package'
[IO.Compression.ZipFile]::ExtractToDirectory($packagePath, $extractedPath)
$packagedAssembly = Join-Path $extractedPath 'CrestronHomeNUnit.Driver.dll'
$compiledPatch = Join-Path $driverOutput 'patched\CrestronHomeNUnit.Driver.dll'
if ((Get-FileHash -LiteralPath $compiledPatch).Hash -ne (Get-FileHash -LiteralPath $packagedAssembly).Hash) { throw 'The package does not contain the expected patched DLL.' }

foreach ($suite in @('self-tests', 'compatibility')) {
    $resultsDirectory = Join-Path $resultRoot $suite
    $options = @()
    $runs = @($resultsDirectory)
    if ($suite -eq 'self-tests') {
        $options = @('--repeat')
        $runs = @((Join-Path $resultsDirectory 'run-1'), (Join-Path $resultsDirectory 'run-2'))
    }
    & $validator $resultsDirectory $suite $packagedAssembly @options > (Join-Path $resultRoot "$suite.log") 2>&1
    if ($LASTEXITCODE -ne 0) { throw "$suite failed. See $resultRoot" }
    foreach ($run in $runs) {
        [xml] $results = Get-Content -LiteralPath (Join-Path $run 'TestResult.xml') -Raw
        if ([int]$results.'test-run'.total -le 0 -or [int]$results.'test-run'.failed -ne 0) { throw "$suite returned invalid or failing results." }
        Write-Host ("{0} ({1}): {2} passed, {3} skipped, {4} failed." -f $suite, (Split-Path $run -Leaf), $results.'test-run'.passed, $results.'test-run'.skipped, $results.'test-run'.failed)
    }
}
& $validator (Join-Path $resultRoot 'discovery') self-tests $packagedAssembly --explore > (Join-Path $resultRoot 'discovery.log') 2>&1
if ($LASTEXITCODE -ne 0) { throw 'Packaged discovery failed.' }

$cecilPath = Join-Path $env:USERPROFILE '.dotnet\tools\.store\dotnet-ilrepack\2.0.45\dotnet-ilrepack\2.0.45\tools\net8.0\any\Mono.Cecil.dll'
Add-Type -Path $cecilPath
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($packagedAssembly)
try {
    $prohibited = @($assembly.MainModule.Types | Where-Object { $_.Namespace -eq 'System' -or $_.Namespace.StartsWith('System.') })
    if ($prohibited.Count -ne 0) { throw "Found $($prohibited.Count) prohibited System.* type definitions." }
    if (@($assembly.MainModule.AssemblyReferences | Where-Object Name -Match '^nunit').Count -ne 0) { throw 'The merged package still references an external NUnit assembly.' }
    if ($assembly.MainModule.GetType('NUnitLite.AutoRun')) { throw 'NUnitLite was unexpectedly included in the Home package.' }
}
finally { $assembly.Dispose() }
Write-Host "Packaged DLL and discovery verified on Windows. Results: $resultRoot"
