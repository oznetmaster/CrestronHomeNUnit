# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE file in the project root for full license information.

param(
	[string] $ProjectUserFile = "$PSScriptRoot\CrestronHomeNUnit.Driver\CrestronHomeNUnit.Driver.csproj.user",
	[ValidateSet('Debug', 'Release')][string] $Configuration = 'Debug',
	[int] $Port = 0
)
$ErrorActionPreference = 'Stop'
[xml] $settings = Get-Content -LiteralPath $ProjectUserFile -Raw
function Get-Setting([string] $name) {
	$node = $settings.SelectSingleNode("/Project/PropertyGroup/$name")
	if (-not $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) { throw "Set $name in $ProjectUserFile" }
	return $node.InnerText
}
$processorAddress = Get-Setting 'CrestronHomeIP'
$user = Get-Setting 'CrestronHomeFtpUser'
$password = Get-Setting 'CrestronHomeSftpPassword'
# Keep credentials outside the repository, encrypted for the current Windows account.
$vaultDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CrestronHomeNUnit'
$vaultPath = Join-Path $vaultDirectory 'ProcessorKeys.dat'
$serializer = [Runtime.Serialization.Json.DataContractJsonSerializer]::new([Collections.Generic.Dictionary[string,string]])
$values = [Collections.Generic.Dictionary[string,string]]::new()
if (Test-Path -LiteralPath $vaultPath) {
	$bytes = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($vaultPath), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
	$inputStream = [IO.MemoryStream]::new($bytes)
	try { $values = $serializer.ReadObject($inputStream) } finally { $inputStream.Dispose() }
}
$identity = 'host:' + $processorAddress.ToLowerInvariant()
$values['user:' + $identity] = $user
$values['password:' + $identity] = $password
$stream = [IO.MemoryStream]::new()
try {
	$serializer.WriteObject($stream, $values)
	[IO.Directory]::CreateDirectory($vaultDirectory) | Out-Null
	[IO.File]::WriteAllBytes($vaultPath, [Security.Cryptography.ProtectedData]::Protect($stream.ToArray(), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser))
} finally { $stream.Dispose() }
$settingsPath = Join-Path $vaultDirectory 'Runner.local.json'
$json = [ordered]@{ Host = $processorAddress; Port = $Port; User = $user } | ConvertTo-Json
[IO.File]::WriteAllText($settingsPath, $json, [Text.UTF8Encoding]::new($false))
Write-Host 'Processor credentials imported into Windows-protected local storage. Use Find packages, select a package, then Connect to authenticate over SFTP.'