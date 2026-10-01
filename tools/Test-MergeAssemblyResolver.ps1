# Copyright (c) 2026 Neil Colvin. MIT; see LICENSE.
param([Parameter(Mandatory)][string]$ToolDirectory, [Parameter(Mandatory)][string]$ResultsDirectory)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($ResultsDirectory)
if(Test-Path $root){throw 'Use a fresh results directory'}
New-Item -ItemType Directory -Path "$root/dependency","$root/subject","$root/caller"|Out-Null
'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net472</TargetFramework><LangVersion>latest</LangVersion></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" /></ItemGroup></Project>' | Set-Content "$root/dependency/dependency.csproj"
'public enum ExternalLevel { One } public sealed class ExternalAttribute : System.Attribute { public ExternalAttribute(ExternalLevel value) {} }'|Set-Content "$root/dependency/Types.cs"
'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net472</TargetFramework><LangVersion>latest</LangVersion></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" /><ProjectReference Include="../dependency/dependency.csproj" /></ItemGroup></Project>' | Set-Content "$root/subject/subject.csproj"
'[External(ExternalLevel.One)] public static class Subject { public static object Create() => new { Value = 1 }; }'|Set-Content "$root/subject/Types.cs"
& dotnet build "$root/subject/subject.csproj" -c Release *> "$root/build.log"
if($LASTEXITCODE){throw 'Regression fixture build failed'}
$lib="$root/subject/bin/Release/net472"
@("$lib/subject.dll","$lib/dependency.dll")|Set-Content "$root/inputs.txt"
Push-Location "$root/caller"
try {
 & "$PSScriptRoot/../CrestronHomeNUnit.Driver/ILRepackMerge.ps1" -TargetPath "$lib/subject.dll" -OutputPath "$root/merged/subject.dll" -InputListFile "$root/inputs.txt" -LibDir $lib -ToolDirectory $ToolDirectory -FxRuntimeDir "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319" *> "$root/merge.log"
} finally {Pop-Location}
if(!(Test-Path "$root/merged/subject.dll")){throw 'Merged fixture is missing'}
'Cross-assembly enum attribute and anonymous type merged from an unrelated working directory.'|Set-Content "$root/passed.txt"
