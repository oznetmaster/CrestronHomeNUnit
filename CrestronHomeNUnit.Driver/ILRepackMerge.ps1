# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE file in the project root for full license information.

# Adapted from the user's Entity V2 ILRepackMerge.ps1 workflow.
param(
    [Parameter(Mandatory)][string] $TargetPath,
    [Parameter(Mandatory)][string] $OutputPath,
    [Parameter(Mandatory)][string] $InputListFile,
    [Parameter(Mandatory)][string] $LibDir,
    [Parameter(Mandatory)][string] $ToolDirectory,
    [string] $SdkLibDir,
    [string] $FxRefDir,
    [string] $FxRuntimeDir
)
$ErrorActionPreference = 'Stop'
$inputs = @(Get-Content -LiteralPath $InputListFile | Where-Object { $_ -ne '' } | Select-Object -Unique)
if ($inputs.Count -eq 0 -or $inputs[0] -ne $TargetPath) { throw 'The driver must be the first merge input.' }
foreach ($inputPath in $inputs) {
    if (-not (Test-Path -LiteralPath $inputPath)) { throw "Missing merge input: $inputPath" }
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputPath))) | Out-Null
# Private resource helpers and compiler-generated anonymous types are scoped to
# their original assembly. /allowdup must not combine unrelated types that happen
# to have the same name. Rewrite disposable inputs, retaining the original files.
Add-Type -Path (Join-Path $ToolDirectory 'Mono.Cecil.dll')
$preparedDirectory = Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputPath))) 'inputs'
[IO.Directory]::CreateDirectory($preparedDirectory) | Out-Null
$preparedInputs = @()
$resourceHelperCount = 0
$anonymousTypeShapes = @{}
foreach ($inputPath in $inputs) {
    $inputAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($inputPath)
    try {
        $module = $inputAssembly.MainModule
        $renames = @{}
        foreach ($type in $module.Types) {
            if ($type.IsPublic) { continue }
            if ($type.FullName -eq 'System.SR') {
                $renames[$type.FullName] = '_MergedResources.' + $inputAssembly.Name.Name
                $resourceHelperCount++
            }
            elseif ($type.Name -match '^<>f__AnonymousType[0-9]+' -and
                    ($type.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'System.Runtime.CompilerServices.CompilerGeneratedAttribute' })) {
                $privateNamespace = '_MergedAnonymousTypes.' + $inputAssembly.Name.Name
                $renames[$type.FullName] = $privateNamespace
                $anonymousTypeShapes[$privateNamespace + '.' + $type.Name] = ($type.Properties.Name | Sort-Object) -join '|'
            }
        }
        if ($renames.Count -eq 0) {
            $preparedInputs += $inputPath
            continue
        }
        foreach ($reference in $module.GetTypeReferences()) {
            $localScope = $reference.Scope -eq $module -or
                ($reference.Scope -is [Mono.Cecil.AssemblyNameReference] -and $reference.Scope.Name -eq $inputAssembly.Name.Name)
            if ($localScope -and $renames.ContainsKey($reference.FullName)) { $reference.Namespace = $renames[$reference.FullName] }
        }
        foreach ($type in $module.Types) {
            if ($renames.ContainsKey($type.FullName)) { $type.Namespace = $renames[$type.FullName] }
        }
        $preparedPath = Join-Path $preparedDirectory ([IO.Path]::GetFileName($inputPath))
        $inputAssembly.Write($preparedPath)
        $preparedInputs += $preparedPath
    }
    finally { $inputAssembly.Dispose() }
}
Write-Output "Isolated $resourceHelperCount private resource helpers and $($anonymousTypeShapes.Count) anonymous types before merging."
$mergeArguments = @('/internalize', '/allowdup', '/allowduplicateresources', "/out:$OutputPath")
foreach ($directoryPath in @($LibDir, $SdkLibDir, $FxRefDir, $FxRuntimeDir)) {
    if ($directoryPath -and (Test-Path -LiteralPath $directoryPath)) { $mergeArguments += "/lib:$directoryPath" }
}
$mergeArguments += $preparedInputs
& dotnet (Join-Path $ToolDirectory 'ILRepackTool.dll') @mergeArguments
if ($LASTEXITCODE -ne 0) { throw "ILRepack failed with exit code $LASTEXITCODE" }

# Verify reflection-visible anonymous properties as well as successful compilation.
# Incorrect duplicate merging can appear to work on desktop CLR but fail on Mono.
$mergedAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($OutputPath)
try {
    foreach ($typeName in $anonymousTypeShapes.Keys) {
        $mergedType = $mergedAssembly.MainModule.GetType($typeName)
        if (!$mergedType -or (($mergedType.Properties.Name | Sort-Object) -join '|') -cne $anonymousTypeShapes[$typeName]) {
            throw "ILRepack changed the anonymous type's properties: $typeName"
        }
    }
}
finally { $mergedAssembly.Dispose() }
