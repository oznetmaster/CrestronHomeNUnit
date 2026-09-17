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
$archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    foreach ($assembly in @('TestAdapter', 'Workflow', 'Client', 'Transport', 'Android')) {
        if ($null -eq $archive.GetEntry("lib/net10.0/CrestronHomeNUnit.$assembly.dll")) {
            throw "Adapter package is missing its $assembly implementation assembly."
        }
    }
} finally { $archive.Dispose() }
$root = Join-Path ([IO.Path]::GetTempPath()) ('adapter-acceptance-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$manifestEnvironment = 'CRESTRON_ADAPTER_ACCEPTANCE_' + [Guid]::NewGuid().ToString('N')
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject><IsPackable>false</IsPackable></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.9.0" PrivateAssets="all" />
    <PackageReference Include="CrestronHomeNUnit.TestAdapter" Version="$Version" PrivateAssets="all" />
    <PackageReference Include="NUnit" Version="4.6.1" PrivateAssets="all" />
    <PackageReference Include="NUnit3TestAdapter" Version="6.3.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $root 'Consumer.csproj'), $project)
$androidConsumer = @'
// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using CrestronHomeNUnit.Android;
using NUnit.Framework;
using System.Xml.Linq;
namespace PackageAcceptance;
public sealed class AndroidHelpers
{
    [Test]
    public void PackagedAndroidApiCanInspectRepeatedControls()
    {
        const string app = "com.crestron.phoenix.app";
        string Node(string id, string text, string children = "") =>
            "<node package='" + app + "' resource-id='" + app + ":id/" + id +
            "' text='" + text + "' enabled='true' bounds='[0,0][100,100]'>" + children + "</node>";
        string Row(string title, string state, string action) => Node("row", "",
            Node("customdevice_statusAndButtonTitleSubtitle", "", Node("titleSubtitle_title", title) + Node("titleSubtitle_subtitle", state)) +
            Node("customdevice_statusAndButtonAction", action));
        var hierarchy = new AndroidHierarchy("<hierarchy>" + Node("customdevices_toolbarTitle", "Options") +
            Node("customdevices_toolbarClose", "") + Row("First", "ON", "Turn Off") + Row("Second", "OFF", "Turn On") + "</hierarchy>", app);
        CrestronHomePages.RequireExtensionPage(hierarchy, "Options");
        Assert.That(CrestronHomePages.ReadStatusAndButton(hierarchy, "First"), Is.EqualTo(("ON", "Turn Off", true)));
        Assert.That(CrestronHomePages.ReadStatusAndButton(hierarchy, "Second"), Is.EqualTo(("OFF", "Turn On", true)));
        var controls = new AndroidHierarchy("<hierarchy>" +
            Node("row", "", Node("label", "First") + Node("adjust", "+")) +
            Node("row", "", Node("label", "Second") + Node("adjust", "+")) + "</hierarchy>", app);
        Assert.Throws<System.InvalidOperationException>(() => controls.RequireUnique(CrestronHomePages.Resource("adjust")));
        Assert.That(controls.RequireUnique(CrestronHomePages.Resource("adjust") with { SiblingText = "Second" }).Text, Is.EqualTo("+"));
        XElement Page(string id, string title) => new XElement("node",
            new XAttribute("package", app), new XAttribute("resource-id", app + ":id/" + id),
            new XAttribute("class", "android.widget.LinearLayout"),
            XElement.Parse(Node("customdevices_toolbarTitle", title)), XElement.Parse(Node("customdevices_toolbarClose", "")));
        var container = new XElement("node", new XAttribute("package", app), new XAttribute("resource-id", app + ":id/main_container"),
            Page("customdevice_pulley", "Room"), Page("", "Schedule"));
        var nested = new AndroidHierarchy(new XElement("hierarchy", container).ToString(), app);
        var front = CrestronHomeExtensionPages.RequirePage(nested, new[] { "Room", "Schedule" });
        Assert.That(front.RequireUnique(CrestronHomePages.Resource("customdevices_toolbarTitle")).Text, Is.EqualTo("Schedule"));
        Assert.Throws<System.InvalidOperationException>(() => CrestronHomeExtensionPages.RequirePage(nested, new[] { "Room" }));
        Assert.That(typeof(CrestronHomeNavigation).GetMethod(nameof(CrestronHomeNavigation.InspectRoomExtensionPagesAsync)), Is.Not.Null);
        Assert.That(typeof(CrestronHomeExtensionNavigation).GetMethod(nameof(CrestronHomeExtensionNavigation.InspectSelectionAsync)), Is.Not.Null);
        var childPlan = new CrestronHomeNUnit.Workflow.AndroidTestPlan("unused", "unused")
        {
            ManagedChildren = new[] { new CrestronHomeNUnit.Workflow.AndroidManagedChildPlan("room", "child-id", "CI Child", "Example Child", 3) }
        };
        Assert.That(childPlan.ManagedChildren[0].Alias, Is.EqualTo("room"));
        var context = new AndroidRunContext(1, "package-test", "unused", 1, 1, "192.0.2.1", 7,
            "11111111-1111-1111-1111-111111111111", "1.0.0.1", new string('a', 64), new string('b', 64),
            new AndroidSessionProfile("unused", "unused", "example.app", "Example Home", "unused"), "unused")
        {
            ManagedDevices = new[] { new AndroidManagedDeviceBinding("room", 19, 7, "Example Child", "CI Child", 3) }
        };
        Assert.That(context.RequireManagedDevice("room").DeviceId, Is.EqualTo(19));
        Assert.Throws<System.IO.InvalidDataException>(() => context.RequireManagedDevice("missing"));
    }
}
'@
[IO.File]::WriteAllText((Join-Path $root 'AndroidHelpers.cs'), $androidConsumer)
[IO.File]::WriteAllText((Join-Path $root 'Workflows.xml'), "<Workflows><Workflow id='acceptance' name='Packaged workflow acceptance' settingsEnvironment='$manifestEnvironment' /></Workflows>")
$escapedSource = [Security.SecurityElement]::Escape($packageDirectoryPath)
[IO.File]::WriteAllText((Join-Path $root 'NuGet.Config'), "<configuration><packageSources><clear/><add key='release' value='$escapedSource'/><add key='nuget.org' value='https://api.nuget.org/v3/index.json'/></packageSources><packageSourceMapping><packageSource key='release'><package pattern='CrestronHomeNUnit.TestAdapter'/></packageSource><packageSource key='nuget.org'><package pattern='*'/></packageSource></packageSourceMapping></configuration>")
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
    # Exercise the packaged Android API through NUnit in the same project as the
    # workflow adapter. No source checkout, Android profile or processor is used.
    $androidExecution = @(dotnet test Consumer.csproj --no-build --no-restore --filter 'FullyQualifiedName=PackageAcceptance.AndroidHelpers.PackagedAndroidApiCanInspectRepeatedControls' --logger 'trx;LogFileName=android.trx' --results-directory results 2>&1)
    $androidExecution | Set-Content android.log
    if ($LASTEXITCODE -ne 0) { throw "Packaged NUnit Android consumer failed. See $root." }
    [xml]$androidResult = Get-Content results/android.trx -Raw
    if ([int]$androidResult.TestRun.ResultSummary.Counters.passed -ne 1 -or [int]$androidResult.TestRun.ResultSummary.Counters.total -ne 1) { throw 'Expected exactly one passing packaged Android consumer test.' }
    # The random environment variable is unset. Execution must fail before contacting any processor.
    $execution = @(dotnet test Consumer.csproj --no-build --no-restore --filter 'FullyQualifiedName=CrestronHome.Workflows.acceptance' --logger 'trx;LogFileName=negative.trx' --results-directory results 2>&1)
    $exitCode = $LASTEXITCODE
    $execution | Set-Content execution.log
    if ($exitCode -ne 1) { throw 'Missing private configuration must fail the selected workflow.' }
    [xml]$result = Get-Content results/negative.trx -Raw
    if ([int]$result.TestRun.ResultSummary.Counters.failed -ne 1 -or [int]$result.TestRun.ResultSummary.Counters.total -ne 1) { throw 'Expected one failed workflow result.' }
    # Exercise the real Android regression fixtures against the extracted package,
    # without referencing or rebuilding any production source project. Keep the
    # friend-test assembly name so internal navigation guards receive coverage.
    $androidRoot = Join-Path $root 'android-regressions'
    [IO.Directory]::CreateDirectory($androidRoot) | Out-Null
    $testSources = Join-Path (Split-Path $PSScriptRoot) 'CrestronHomeNUnit.Android.Tests'
    Get-ChildItem -LiteralPath $testSources -Filter '*.cs' -File | Copy-Item -Destination $androidRoot
    $escapedCache = [Security.SecurityElement]::Escape((Join-Path $root 'packages'))
    $androidProject = @"
<!-- Copyright (c) 2026 Neil Colvin. Licensed under the MIT License. -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject><IsPackable>false</IsPackable>
    <AssemblyName>CrestronHomeNUnit.Android.Tests</AssemblyName><ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable><LangVersion>latest</LangVersion><TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <RestorePackagesPath>$escapedCache</RestorePackagesPath>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.9.0" />
    <PackageReference Include="CrestronHomeNUnit.TestAdapter" Version="$Version" PrivateAssets="all" />
    <PackageReference Include="NUnit" Version="4.6.1" />
    <PackageReference Include="NUnit3TestAdapter" Version="6.3.0" />
  </ItemGroup>
</Project>
"@
    $androidProjectPath = Join-Path $androidRoot 'AndroidPackageTests.csproj'
    [IO.File]::WriteAllText($androidProjectPath, $androidProject)
    & (Join-Path $PSScriptRoot 'Test-DiscoveredCoverage.ps1') -Stage Desktop -Project $androidProjectPath -Framework net10.0 -RequiredCategories @('unit') -ResultsDirectory (Join-Path $root 'android-coverage')
    if ($LASTEXITCODE -ne 0) { throw "Packaged Android regression coverage failed. See $root." }
    # Source mapping pins this adapter to the local feed; byte comparison also
    # proves that execution used this archive rather than a same-version cache.
    $archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        foreach ($assembly in @('TestAdapter', 'Workflow', 'Client', 'Transport', 'Android')) {
            $entry = $archive.GetEntry("lib/net10.0/CrestronHomeNUnit.$assembly.dll")
            $stream = $entry.Open()
            try { $expectedHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            $executedFile = Join-Path $androidRoot "bin/Release/net10.0/CrestronHomeNUnit.$assembly.dll"
            if ((Get-FileHash -LiteralPath $executedFile -Algorithm SHA256).Hash -cne $expectedHash) { throw "Packaged $assembly assembly bytes differ from the tested output." }
        }
    } finally { $archive.Dispose() }
    Write-Host 'Packaged adapter: isolated restore, workflow discovery, NUnit Android API use, complete Android regression coverage, archive byte verification and fail-closed execution passed.'
    Write-Host "Private acceptance evidence: $root"
} finally { Pop-Location }

# The negative test intentionally returns 1. Hosted PowerShell steps propagate
# LASTEXITCODE, so clear that expected status only after every assertion passed.
$global:LASTEXITCODE = 0
