# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.

$ErrorActionPreference = 'Stop'
if (-not $env:GITHUB_ACTIONS) { throw 'This tool setup is for GitHub Actions.' }
dotnet tool install --global dotnet-ilrepack --version 2.0.45
if ($LASTEXITCODE -ne 0) { throw 'ILRepack installation failed.' }
$root = Join-Path $env:RUNNER_TEMP 'crestron-packaging-tools'
foreach ($package in @(
    @{ Name = 'Crestron.DeviceDrivers.ManifestUtil'; Version = '27.0.24' },
    @{ Name = 'Microsoft.Office.Interop.Word'; Version = '15.0.4797.1004' },
    @{ Name = 'Microsoft.Office.Core'; Version = '12.0.0' }
)) {
    nuget install $package.Name -Version $package.Version -OutputDirectory $root -NonInteractive -Source https://api.nuget.org/v3/index.json
    if ($LASTEXITCODE -ne 0) { throw "Installation failed: $($package.Name)" }
}
$manifest = Get-ChildItem (Join-Path $root 'Crestron.DeviceDrivers.ManifestUtil.27.0.24') -Filter ManifestUtil.exe -Recurse | Select-Object -First 1
$word = Get-ChildItem (Join-Path $root 'Microsoft.Office.Interop.Word.15.0.4797.1004') -Filter Microsoft.Office.Interop.Word.dll -Recurse | Select-Object -First 1
$office = Get-ChildItem (Join-Path $root 'Microsoft.Office.Core.12.0.0') -Filter office.dll -Recurse | Select-Object -First 1
if (-not $manifest -or -not $word -or -not $office) { throw 'Packaging tool files are missing.' }
Copy-Item -LiteralPath $word.FullName -Destination $manifest.Directory.FullName
Copy-Item -LiteralPath $office.FullName -Destination $manifest.Directory.FullName
$configuration = @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <runtime>
    <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
      <dependentAssembly>
        <assemblyIdentity name="office" publicKeyToken="71e9bce111e9429c" culture="neutral" />
        <bindingRedirect oldVersion="0.0.0.0-15.0.0.0" newVersion="12.0.0.0" />
      </dependentAssembly>
    </assemblyBinding>
  </runtime>
</configuration>
'@
[IO.File]::WriteAllText((Join-Path $manifest.Directory.FullName 'ManifestUtil.exe.config'), $configuration)
"MANIFEST_UTIL_EXE=$($manifest.FullName)" | Out-File $env:GITHUB_ENV -Append -Encoding utf8
