# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE file in the project root for full license information.

param(
    [Parameter(Mandatory)][string] $TestProject,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]{0,58}$')][string] $SuiteId,
    [Parameter(Mandatory)][string] $TestNamespace,
    [Parameter(Mandatory)][string] $DisplayName,
    [ValidateRange(0,65535)][int] $Port = 0,
    [string] $Manufacturer = 'Neil Colvin',
    [int] $ExpectedUnitTestCount = 0,
    [switch] $IncludeLiveTests,
    [string] $Solution
)
$ErrorActionPreference = 'Stop'
if ($Solution) { $Solution = (Resolve-Path -LiteralPath $Solution).Path }
$testProjectPath = (Resolve-Path -LiteralPath $TestProject).Path
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $destination) { throw 'The output directory already exists; existing package projects are never overwritten.' }
$assemblyName = Split-Path -Leaf $destination
if ($assemblyName -notmatch '^[A-Za-z][A-Za-z0-9.]*$') { throw 'Use an output directory name containing only letters, digits and dots.' }
[xml]$testProjectXml = Get-Content -LiteralPath $testProjectPath -Raw
if ($testProjectXml.OuterXml -notmatch 'net472') { throw 'The test project must target net472.' }
$utf8 = [Text.UTF8Encoding]::new($false)
function Write-PackageFile([string]$relative, [string]$content) {
    $path = Join-Path $destination $relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    [IO.File]::WriteAllText($path, ($content -replace '\r?\n', "`r`n"), $utf8)
}
function Escape-Xml([string]$value) { [Security.SecurityElement]::Escape($value) }
$sdkPath = Escape-Xml ([IO.Path]::GetRelativePath($destination, $PSScriptRoot))
$testPath = Escape-Xml ([IO.Path]::GetRelativePath($destination, $testProjectPath))
$project = @'
<!-- Copyright (c) 2026 Neil Colvin. MIT License; see the CrestronHomeNUnit LICENSE. -->
<Project>
  <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />
  <Import Project="$(MSBuildProjectName).Local.targets" Condition="Exists('$(MSBuildProjectName).Local.targets')" />
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>false</IsTestProject>
    <ProcessorTestSdkRoot Condition="'$(ProcessorTestSdkRoot)' == ''">$(MSBuildProjectDirectory)\__SDK__</ProcessorTestSdkRoot>
    <BuildProcessorTestPackages Condition="'$(BuildProcessorTestPackages)' == '' And '$(BuildingInsideVisualStudio)' == 'true'">true</BuildProcessorTestPackages>
    <BuildProcessorTestPackages Condition="'$(BuildProcessorTestPackages)' == ''">false</BuildProcessorTestPackages>
    <AssemblyName>__ASSEMBLY__</AssemblyName>
    <RootNamespace>CrestronHomeNUnit.Driver</RootNamespace>
    <Version>0.1.0</Version>
  </PropertyGroup>
  <Import Project="$(ProcessorTestSdkRoot)\ProcessorTestPackage\ProcessorTestPackage.props" Condition="'$(BuildProcessorTestPackages)' == 'true' And Exists('$(ProcessorTestSdkRoot)\ProcessorTestPackage\ProcessorTestPackage.props')" />
  <ItemGroup Condition="'$(BuildProcessorTestPackages)' == 'true'">
    <ProjectReference Include="__TEST__" />
  </ItemGroup>
  <Import Project="$(ProcessorTestSdkRoot)\ProcessorTestPackage\ProcessorTestPackage.targets" Condition="'$(BuildProcessorTestPackages)' == 'true' And Exists('$(ProcessorTestSdkRoot)\ProcessorTestPackage\ProcessorTestPackage.targets')" />
  <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />
  <PropertyGroup Condition="'$(BuildProcessorTestPackages)' != 'true'">
    <BuildDependsOn>SkipProcessorPackage</BuildDependsOn>
    <RebuildDependsOn>SkipProcessorPackage</RebuildDependsOn>
  </PropertyGroup>
  <Target Name="SkipProcessorPackage">
    <Message Importance="high" Text="Processor packaging skipped. Build in Visual Studio or set BuildProcessorTestPackages=true on Windows." />
  </Target>
  <Target Name="CheckProcessorTestSdk" BeforeTargets="PrepareForBuild" Condition="'$(BuildProcessorTestPackages)' == 'true'">
    <Error Condition="!Exists('$(ProcessorTestSdkRoot)\ProcessorTestPackage\ProcessorTestPackage.targets')" Text="Set ProcessorTestSdkRoot to the CrestronHomeNUnit checkout containing the processor package SDK." />
  </Target>
</Project>
'@
Write-PackageFile "$assemblyName.csproj" ($project.Replace('__SDK__',$sdkPath).Replace('__ASSEMBLY__',$assemblyName).Replace('__TEST__',$testPath))
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'CrestronHomeNUnit.Driver\CrestronHomeNUnit.Driver.json') -Raw | ConvertFrom-Json
$manifest.GeneralInformation.Guid = [guid]::NewGuid().ToString()
$manifest.GeneralInformation.DeviceType = 'Utility'
$manifest.GeneralInformation.Manufacturer = $Manufacturer
$manifest.GeneralInformation.BaseModel = $DisplayName
$manifest.GeneralInformation.Developer.Company = $Manufacturer
$manifest.GeneralInformation.Developer.Contact = $Manufacturer
$manifest.GeneralInformation.DriverVersion = '0.1.000.0000'
$manifest.GeneralInformation.VersionDate = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss.fff')
Write-PackageFile "$assemblyName.json" ($manifest | ConvertTo-Json -Depth 10)
$namespacePattern = Escape-Xml ('^' + [regex]::Escape($TestNamespace) + '($|[.])')
$configuration = [ordered]@{
    Name = $DisplayName
    Port = $Port
    Suites = @(
        [ordered]@{ Id=$SuiteId; Name="$DisplayName — Unit Tests"; FilterXml="<filter><and><namespace re='1'>$namespacePattern</namespace><not><cat>Live</cat></not></and></filter>"; ManualOnly=$false; ExpectedCount=$ExpectedUnitTestCount },
        [ordered]@{ Id="$SuiteId-live"; Name="$DisplayName — Live Tests"; FilterXml="<filter><and><namespace re='1'>$namespacePattern</namespace><cat>Live</cat></and></filter>"; ManualOnly=$true; ExpectedCount=0 }
    )
}
if (!$IncludeLiveTests) { $configuration.Suites = @($configuration.Suites[0]) }
Write-PackageFile 'ProcessorTests.json' ($configuration | ConvertTo-Json -Depth 8)
$ui = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'CrestronHomeNUnit.Driver\IncludeInPkg\UiDefinitions\UiDefinition.xml')).Replace('command:runCompatibilityTests','command:runAdditionalTests')
if (!$IncludeLiveTests) { $ui = $ui -replace '(?m)^.*id="Compatibility".*\r?\n','' }
$ui = $ui.Replace('<statusandbutton id="Compatibility" label="^Compatibility" status="{compatibilityResult}" buttonlabel="^Run" buttonaction="command:runAdditionalTests" />', '<textdisplay id="Compatibility" title="^Compatibility" line1label="{compatibilityResult}" line2label="Select live tests in the Windows runner" />')
Write-PackageFile 'IncludeInPkg\UiDefinitions\UiDefinition.xml' $ui
$translations = [ordered]@{ Title=$DisplayName; Status='Test Host'; Suite='Unit Tests'; Compatibility='Live Tests (changes device state)'; Run='Run Tests'; Discovery='Unit Test Discovery'; Discover='Discover Tests'; DesktopConnection='Desktop Runner' }
Write-PackageFile 'IncludeInPkg\Translations\en-US.json' ($translations | ConvertTo-Json)
$driverIdentity = ($Manufacturer -replace '[^a-zA-Z0-9]','').ToLowerInvariant() + '.' + ($DisplayName -replace '[^a-zA-Z0-9]','').ToLowerInvariant() + '.cloud.' + ($Manufacturer -replace '[^a-zA-Z0-9]','').ToLowerInvariant()
$prepare = @'
# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License; see the CrestronHomeNUnit LICENSE.
param([ValidateSet('Debug','Release')][string]$Configuration='Debug')
$project = Join-Path $PSScriptRoot '__ASSEMBLY__.csproj'
[xml]$xml = Get-Content -LiteralPath $project -Raw
$sdk = $xml.SelectSingleNode("/Project/PropertyGroup/ProcessorTestSdkRoot").InnerText
$sdk = $sdk.Replace('$(MSBuildProjectDirectory)', $PSScriptRoot)
& (Join-Path $sdk 'PrepareDesktopRunner.ps1') -ProjectUserFile ($project + '.user') -Configuration $Configuration -Port __PORT__
'@
Write-PackageFile 'PrepareRunner.ps1' ($prepare.Replace('__ASSEMBLY__',$assemblyName).Replace('__IDENTITY__',$driverIdentity).Replace('__PORT__',[string]$Port))
$readme = @'
# __DISPLAY__ processor package

Open the repository solution and build `__ASSEMBLY__.csproj` in Visual Studio. Command-line builds skip processor packaging unless `-p:BuildProcessorTestPackages=true` is supplied, so ordinary CI builds do not require the processor SDK. The package is written to `bin\Debug\net472\__ASSEMBLY__.pkg`.

The project imports the shared host and build tools from `ProcessorTestSdkRoot`. Override that property in `__ASSEMBLY__.Local.targets` if the SDK checkout is elsewhere. Source tests remain in the referenced test project; no fixture copies are maintained here. NUnit and all application dependencies are merged; Crestron SDK libraries remain platform dependencies.

The build stages the Home UI, validates discovery from the merged and patched assembly, constructs the package, and normalizes its ZIP entries. Debug builds increment the build version; Release version increments are reserved for CI. Auto-deployment uses the existing shared SFTP script and the local `.csproj.user` settings, only for Debug builds inside Visual Studio.

Driver name: **__DISPLAY__**. Manufacturer: **__MANUFACTURER__**. Category: **Utility**. TCP port: **automatic by default** (`Port=0`). Find the package using **Find packages** in the runner; the Home tile shows its current port for manual connections.

The Windows runner discovers the suites declared in ProcessorTests.json. Use -IncludeLiveTests when generating a package with a separate Live category. Unit Tests are selected by default. Live Tests require explicit selection and may operate physical devices.

Choose configuration files using **Test inputs…** in the Windows runner before discovery or execution. Inputs are kept outside the package and sent over an authenticated, encrypted configuration channel. Each suite has its own inputs. Tests locate files through `TestContext.Parameters.Get("TestDataDirectory", AppContext.BaseDirectory)`. The test suite must reload its configuration between operations rather than cache the first discovery indefinitely.

Supply the files required by the live suite using **Test inputs…**. Fixture configuration must honor `TestContext.Parameters.Get ("EnableLiveTests", configuredDefault)` so explicit selection overrides any legacy JSON enable switch. The runner supplies the NUnit `EnableLiveTests` parameter for the selected live suite, overriding the JSON switch for this operation only. The JSON `Enabled` value can remain false. Secrets are never automatically embedded in the `.pkg`. Transferred files reside in the driver's private data directory and remain stored until replaced or cleared through the runner. Live runs cannot be started from the Home tile.

Build the latest Windows runner before connecting. After deployment and activation, `PrepareRunner.ps1` imports existing deployment credentials into Windows-protected local storage. Use **Find packages** and select a package. It connects automatically when processor credentials are available; otherwise enter the SFTP username and password and use **Connect**. The Home tile displays the current TCP port; there is no pairing key to enter.

The package validator discovers every suite, but its optional `--run-twice` check executes only suites whose `ManualOnly` property is false. Do not remove that property from hardware or other opt-in suites.
'@
Write-PackageFile 'README.md' ($readme.Replace('__ASSEMBLY__',$assemblyName).Replace('__DISPLAY__',$DisplayName).Replace('__MANUFACTURER__',$Manufacturer).Replace('__PORT__',[string]$Port))
Write-Host "Created $destination. Add $assemblyName.csproj to your Visual Studio solution."

# These rules are local to this checkout, rather than tracked .gitignore policy.
$exclude = & git -C $destination rev-parse --path-format=absolute --git-path info/exclude 2>$null
if ($LASTEXITCODE -eq 0) {
    $existing = if (Test-Path -LiteralPath $exclude) { [IO.File]::ReadAllText($exclude) } else { '' }
    foreach ($pattern in @('**/Runner.local.json', '**/Runner.inputs.local.json', '**/LiveTestSettings.json', '**/*.csproj.user', '**/*.Local.targets')) {
        if (!$existing.Contains($pattern)) { $existing += "`r`n$pattern" }
    }
    [IO.File]::WriteAllText($exclude, $existing, $utf8)
}
$solutions = if ($Solution) { @(Get-Item -LiteralPath $Solution) } else { @(Get-ChildItem -LiteralPath ([IO.Path]::GetDirectoryName($destination)) -File | Where-Object { $_.Extension -in '.sln', '.slnx' }) }
if ($solutions.Count -eq 1) {
    & dotnet sln $solutions[0].FullName add (Join-Path $destination "$assemblyName.csproj")
    if ($LASTEXITCODE -ne 0) { throw 'Could not add the package project to the existing solution.' }
} else {
    Write-Host 'Add this project to your existing repository solution. No additional solution is needed.'
}