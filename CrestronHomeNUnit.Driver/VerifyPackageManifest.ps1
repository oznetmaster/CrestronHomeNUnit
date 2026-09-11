# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE file in the project root for full license information.

param(
    [Parameter(Mandatory)][string] $ManifestPath,
    [Parameter(Mandatory)][string] $MetadataPath
)
$ErrorActionPreference = 'Stop'
$expected = (Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json).GeneralInformation
$actual = Get-Content -LiteralPath $MetadataPath -Raw | ConvertFrom-Json
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
