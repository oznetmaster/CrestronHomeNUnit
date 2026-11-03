# Copyright (c) 2026 Neil Colvin. MIT License with Commons Clause; see repository LICENSE.
param(
    [Parameter(Mandatory)][string] $AssemblyPath,
    [Parameter(Mandatory)][string] $OutputPath,
    [Parameter(Mandatory)][string] $CecilPath,
    [Parameter(Mandatory)][string] $InputListFile,
    [string] $ManifestPath
)
$ErrorActionPreference='Stop'
& "$PSScriptRoot/../build/shared/driver-packaging/PatchMergedAssembly.ps1" @PSBoundParameters
