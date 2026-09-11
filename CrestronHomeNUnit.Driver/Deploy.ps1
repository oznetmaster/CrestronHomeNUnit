# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE file in the project root for full license information.

# Adapted from the user's Entity V2 SFTP import workflow.
param(
    [Parameter(Mandatory)][string] $PkgFile,
    [Parameter(Mandatory)][string] $ProjectUserFile
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $PkgFile)) { throw "Package not found: $PkgFile" }
[xml]$settings = Get-Content -LiteralPath $ProjectUserFile -Raw
function Get-Setting([string] $name) {
    $node = $settings.SelectSingleNode("/Project/PropertyGroup/$name")
    if (-not $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) { throw "Set $name in $ProjectUserFile" }
    return $node.InnerText
}
$processorAddress = Get-Setting 'CrestronHomeIP'
$processorUser = Get-Setting 'CrestronHomeFtpUser'
$processorPassword = Get-Setting 'CrestronHomeSftpPassword'
Import-Module Posh-SSH -ErrorAction Stop
$credential = [PSCredential]::new($processorUser, (ConvertTo-SecureString $processorPassword -AsPlainText -Force))
$session = $null
try {
    $session = New-SFTPSession -ComputerName $processorAddress -Credential $credential -Force -ErrorAction Stop
    Set-SFTPItem -SessionId $session.SessionId -Path $PkgFile -Destination '/user/ThirdPartyDrivers/Import' -Force -ErrorAction Stop
    Write-Host 'Package uploaded. Crestron Home will import and upgrade the package normally.'
}
finally {
    if ($session) { Remove-SFTPSession -SessionId $session.SessionId | Out-Null }
}
