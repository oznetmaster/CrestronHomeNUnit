# Copyright (c) 2026 Neil Colvin. MIT licensed.
param([Parameter(Mandatory)][string]$PackageDirectory,[Parameter(Mandatory)][string]$Version)
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+(?:-[a-z0-9.-]+)?$'){throw 'Invalid package version.'}
$package=Join-Path ([IO.Path]::GetFullPath($PackageDirectory)) "CrestronHomeNUnit.TestAdapter.$Version.nupkg"
$archive=[IO.Compression.ZipFile]::OpenRead($package)
try {foreach($extension in @('dll','xml')) {if(!$archive.GetEntry("lib/net10.0/CrestronHomeNUnit.Mac.$extension")){throw "Missing Mac.$extension"}}} finally {$archive.Dispose()}
$root=Join-Path ([IO.Path]::GetTempPath()) ('mac-package-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root)|Out-Null
$source=Split-Path $PSScriptRoot -Parent
Copy-Item "$source/CrestronHomeNUnit.Mac.Tests/MacTests.cs" $root
$feed=[Security.SecurityElement]::Escape([IO.Path]::GetFullPath($PackageDirectory))
@"
<configuration><packageSources><clear/><add key="candidate" value="$feed"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="candidate"><package pattern="CrestronHomeNUnit.TestAdapter"/></packageSource><packageSource key="nuget"><package pattern="*"/></packageSource></packageSourceMapping></configuration>
"@ | Set-Content "$root/NuGet.Config"
# Same tests run against the package. A separate assembly name retains access to transport test seams.
@"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><IsTestProject>true</IsTestProject><AssemblyName>CrestronHomeNUnit.Mac.Tests</AssemblyName><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup><ItemGroup><PackageReference Include="CrestronHomeNUnit.TestAdapter" Version="$Version"/><PackageReference Include="NUnit" Version="5.0.0"/><PackageReference Include="NUnit3TestAdapter" Version="6.3.0"/><PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.9.0"/></ItemGroup></Project>
"@ | Set-Content "$root/Consumer.csproj"
dotnet restore "$root/Consumer.csproj" --packages "$root/packages" --configfile "$root/NuGet.Config"
if($LASTEXITCODE -ne 0){throw 'Fresh Mac package restore failed.'}
dotnet test "$root/Consumer.csproj" -c Release --no-restore --logger trx --results-directory "$root/results"
if($LASTEXITCODE -ne 0){throw 'Packaged Mac helper tests failed.'}
Write-Output "Package validation retained at $root"
