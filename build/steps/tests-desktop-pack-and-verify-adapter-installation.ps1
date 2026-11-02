[xml]$props = Get-Content Version.props -Raw
$version = $props.Project.PropertyGroup.Version
dotnet pack CrestronHomeNUnit.TestAdapter/CrestronHomeNUnit.TestAdapter.csproj -c Release -o artifacts/adapter
if ($LASTEXITCODE -ne 0) { throw 'Adapter package build failed.' }
./tools/Test-AdapterPackage.ps1 -PackageDirectory artifacts/adapter -Version $version
