# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
#Requires -Version 7.0
param(
    [Parameter(Mandatory)][string] $PackageDirectory,
    [Parameter(Mandatory)][string] $Version
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a stable three-part version.' }
$packageDirectoryPath = [IO.Path]::GetFullPath($PackageDirectory)
$packagePath = Join-Path $packageDirectoryPath "CrestronHomeNUnit.TestAdapter.$Version.nupkg"
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) { throw 'Adapter package not found.' }
$root = Join-Path ([IO.Path]::GetTempPath()) ('adapter-acceptance-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$manifestEnvironment = 'CRESTRON_ADAPTER_ACCEPTANCE_' + [Guid]::NewGuid().ToString('N')
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject><IsPackable>false</IsPackable></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.9.0" PrivateAssets="all" />
    <PackageReference Include="CrestronHomeNUnit.TestAdapter" Version="$Version" PrivateAssets="all" />
  </ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $root 'Consumer.csproj'), $project)
[IO.File]::WriteAllText((Join-Path $root 'Workflows.xml'), "<Workflows><Workflow id='acceptance' name='Packaged workflow acceptance' settingsEnvironment='$manifestEnvironment' /></Workflows>")
$escapedSource = [Security.SecurityElement]::Escape($packageDirectoryPath)
[IO.File]::WriteAllText((Join-Path $root 'NuGet.Config'), "<configuration><packageSources><clear/><add key='release' value='$escapedSource'/><add key='nuget.org' value='https://api.nuget.org/v3/index.json'/></packageSources></configuration>")
Push-Location $root
try {
    # Isolate the package cache: a same-version package from an earlier check must not hide missing files.
    dotnet restore Consumer.csproj --configfile NuGet.Config --packages packages --no-http-cache
    if ($LASTEXITCODE -ne 0) { throw 'Clean consumer restore failed.' }
    $discovery = @(dotnet test Consumer.csproj --no-restore --list-tests 2>&1)
    $discovery | Set-Content discovery.log
    if ($LASTEXITCODE -ne 0 -or @($discovery | Where-Object { $_.ToString().Trim() -eq 'Packaged workflow acceptance' }).Count -ne 1) {
        throw "Packaged adapter did not discover exactly one workflow. See $root."
    }
    $sidecar = Join-Path $root 'bin/Debug/net10.0/Consumer.dll.workflow-tests.xml'
    if (-not (Test-Path -LiteralPath $sidecar)) { throw 'Package did not copy the discovery manifest.' }
    # The random environment variable is unset. Execution must fail before contacting any processor.
    $execution = @(dotnet test Consumer.csproj --no-build --no-restore --filter 'FullyQualifiedName=CrestronHome.Workflows.acceptance' --logger 'trx;LogFileName=negative.trx' --results-directory results 2>&1)
    $exitCode = $LASTEXITCODE
    $execution | Set-Content execution.log
    if ($exitCode -ne 1) { throw 'Missing private configuration must fail the selected workflow.' }
    [xml]$result = Get-Content results/negative.trx -Raw
    if ([int]$result.TestRun.ResultSummary.Counters.failed -ne 1 -or [int]$result.TestRun.ResultSummary.Counters.total -ne 1) { throw 'Expected one failed workflow result.' }
    Write-Host 'Packaged adapter: clean restore, offline discovery, manifest copying and fail-closed execution passed.'
    Write-Host "Private acceptance evidence: $root"
} finally { Pop-Location }

# The negative test intentionally returns 1. Hosted PowerShell steps propagate
# LASTEXITCODE, so clear that expected status only after every assertion passed.
$global:LASTEXITCODE = 0
