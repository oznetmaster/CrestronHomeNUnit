# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE file in the project root for full license information.

param(
    [Parameter(Mandatory)][string] $ManifestPath,
    [Parameter(Mandatory, ParameterSetName='Metadata')][string] $MetadataPath,
    [Parameter(Mandatory, ParameterSetName='Package')][string] $PkgPath,
    [Parameter(Mandatory, ParameterSetName='Package')][string] $AssemblyName
)
$ErrorActionPreference = 'Stop'
$expected = (Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json).GeneralInformation
if ($PSCmdlet.ParameterSetName -eq 'Package') {
    if ([string]::IsNullOrWhiteSpace($AssemblyName) -or $AssemblyName.IndexOfAny([char[]]'/\') -ge 0) {
        throw 'A plain assembly name is required for package metadata verification.'
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($PkgPath)
    try {
        $expectedEntry = $AssemblyName + '.dat'
        $entries = @($archive.Entries | Where-Object { [string]::Equals($_.FullName, $expectedEntry, [StringComparison]::OrdinalIgnoreCase) })
        if ($entries.Count -ne 1 -or $entries[0].FullName -cne $expectedEntry) {
            throw 'The package must contain exactly one correctly named root metadata entry.'
        }
        $reader = [System.IO.StreamReader]::new($entries[0].Open())
        try { $actual = $reader.ReadToEnd() | ConvertFrom-Json }
        finally { $reader.Dispose() }
    }
    finally { $archive.Dispose() }
}
else {
    $actual = Get-Content -LiteralPath $MetadataPath -Raw | ConvertFrom-Json
}
$checks = @{
    driverId = [string]$expected.Guid
    driverVersion = [string]$expected.DriverVersion
    deviceType = [string]$expected.DeviceType
    manufacturer = [string]$expected.Manufacturer
    baseModel = [string]$expected.BaseModel
    className = 'CrestronHomeNUnit.Driver.EntryPoint'
}
foreach ($field in $checks.Keys) {
    if ([string]$actual.$field -ne $checks[$field]) {
        throw "Package metadata '$field' does not match the test host. Deployment has been stopped."
    }
}
Write-Output "Verified test host package identity: $($actual.baseModel) $($actual.driverVersion)."
