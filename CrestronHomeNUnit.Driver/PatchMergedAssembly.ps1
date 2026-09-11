# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE file in the project root for full license information.

param(
    [Parameter(Mandatory)][string] $AssemblyPath,
    [Parameter(Mandatory)][string] $OutputPath,
    [Parameter(Mandatory)][string] $CecilPath,
    [Parameter(Mandatory)][string] $InputListFile,
    [string] $ManifestPath
)
$ErrorActionPreference = 'Stop'
Add-Type -Path $CecilPath
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($AssemblyPath)
$module = $assembly.MainModule

# A referenced driver brings its own manifest into the merged assembly. ManifestUtil
# must see only the package owner's manifest, otherwise it can emit the wrong identity.
if ($ManifestPath) {
    $expectedManifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    $expectedGuid = [string]$expectedManifest.GeneralInformation.Guid
    if (-not $expectedGuid) { throw 'The package manifest has no driver GUID.' }
    $ownerCount = 0
    foreach ($resource in @($module.Resources)) {
        if ($resource -isnot [Mono.Cecil.EmbeddedResource] -or -not $resource.Name.EndsWith('.json')) { continue }
        $json = [Text.Encoding]::UTF8.GetString($resource.GetResourceData()).TrimStart([char]0xFEFF)
        try { $document = ConvertFrom-Json -InputObject $json -ErrorAction Stop }
        catch { continue }
        if (-not $document.GeneralInformation.Guid -or -not $document.SchemaVersion) { continue }
        if ([string]$document.GeneralInformation.Guid -eq $expectedGuid) {
            $ownerCount++
        }
        else {
            $module.Resources.Remove($resource) | Out-Null
            Write-Output "Excluded dependency driver manifest from test host: $($resource.Name)"
        }
    }
    if ($ownerCount -ne 1) { throw "Expected one package-owner manifest; found $ownerCount." }
}

# ILRepack 2.0.45 can collapse overloaded named indexer properties while keeping their methods.
# Restore missing property records from the original signature and intact merged accessors.
function Get-AllTypes($types) {
    foreach ($type in $types) {
        $type
        Get-AllTypes $type.NestedTypes
    }
}
$mergedTypes = @{}
foreach ($type in (Get-AllTypes $module.Types)) { $mergedTypes[$type.FullName] = $type }
$indexerCount = 0
foreach ($inputPath in (Get-Content -LiteralPath $InputListFile | Select-Object -Unique)) {
    $inputAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($inputPath)
    try {
        foreach ($originalType in (Get-AllTypes $inputAssembly.MainModule.Types)) {
            $mergedType = $mergedTypes[$originalType.FullName]
            if (-not $mergedType) { continue }
            foreach ($original in $originalType.Properties) {
                if ($original.Parameters.Count -eq 0 -or -not $original.GetMethod) { continue }
                $getter = @($mergedType.Methods | Where-Object FullName -eq $original.GetMethod.FullName)
                if ($getter.Count -ne 1) { continue }
                if (@($mergedType.Properties | Where-Object { $_.GetMethod -eq $getter[0] }).Count -gt 0) { continue }
                # Do not silently discard metadata in unsupported repair cases.
                if ($original.HasCustomAttributes -or $original.HasConstant) {
                    throw "Lost indexer '$($original.FullName)' carries additional metadata; repair must be extended before packaging."
                }
                $property = [Mono.Cecil.PropertyDefinition]::new($original.Name, $original.Attributes, $getter[0].ReturnType)
                $property.GetMethod = $getter[0]
                foreach ($parameter in $getter[0].Parameters) {
                    $property.Parameters.Add([Mono.Cecil.ParameterDefinition]::new($parameter.Name, $parameter.Attributes, $parameter.ParameterType))
                }
                if ($original.SetMethod) {
                    $setter = @($mergedType.Methods | Where-Object FullName -eq $original.SetMethod.FullName)
                    if ($setter.Count -ne 1) { throw "Missing setter for '$($original.FullName)'." }
                    $property.SetMethod = $setter[0]
                }
                $mergedType.Properties.Add($property)
                $indexerCount++
            }
        }
    }
    finally { $inputAssembly.Dispose() }
}
$renames = @{}
foreach ($type in $module.Types) {
    if ($type.Namespace -eq 'System' -or $type.Namespace.StartsWith('System.')) {
        $renames[$type.FullName] = '_Stripped.' + $type.Namespace
    }
}

# Attribute values can contain separate TypeReference objects that are serialized
# as assembly-qualified type names. Renaming TypeDefinition alone misses these.
function Repair-TypeReference([Mono.Cecil.TypeReference] $reference) {
    if ($reference -is [Mono.Cecil.GenericInstanceType]) {
        foreach ($argument in $reference.GenericArguments) { Repair-TypeReference $argument }
    }
    if ($reference -is [Mono.Cecil.TypeSpecification]) {
        Repair-TypeReference $reference.ElementType
        return
    }
    if ($reference.DeclaringType) { Repair-TypeReference $reference.DeclaringType }
    $localScope = $reference.Scope -eq $module -or
        ($reference.Scope -is [Mono.Cecil.AssemblyNameReference] -and $reference.Scope.Name -eq $assembly.Name.Name)
    if ($localScope -and $renames.ContainsKey($reference.FullName)) {
        $reference.Namespace = $renames[$reference.FullName]
    }
}

function Repair-Argument([Mono.Cecil.CustomAttributeArgument] $argument) {
    Repair-TypeReference $argument.Type
    if ($argument.Value -is [Mono.Cecil.TypeReference]) {
        Repair-TypeReference $argument.Value
    }
    elseif ($argument.Value -is [Mono.Cecil.CustomAttributeArgument[]]) {
        foreach ($child in $argument.Value) { Repair-Argument $child }
    }
}

function Repair-Attributes($provider) {
    if ($provider -and $provider.HasCustomAttributes) {
        foreach ($attribute in $provider.CustomAttributes) {
            foreach ($argument in $attribute.ConstructorArguments) { Repair-Argument $argument }
            foreach ($argument in $attribute.Fields) { Repair-Argument $argument.Argument }
            foreach ($argument in $attribute.Properties) { Repair-Argument $argument.Argument }
        }
    }
}

$script:literalCount = 0
function Repair-TypeDefinition([Mono.Cecil.TypeDefinition] $type) {
    Repair-Attributes $type
    foreach ($parameter in $type.GenericParameters) { Repair-Attributes $parameter }
    foreach ($field in $type.Fields) { Repair-Attributes $field }
    foreach ($property in $type.Properties) { Repair-Attributes $property }
    foreach ($event in $type.Events) { Repair-Attributes $event }
    foreach ($method in $type.Methods) {
        Repair-Attributes $method
        Repair-Attributes $method.MethodReturnType
        foreach ($parameter in $method.Parameters) { Repair-Attributes $parameter }
        foreach ($parameter in $method.GenericParameters) { Repair-Attributes $parameter }
        if ($method.HasBody) {
            foreach ($instruction in $method.Body.Instructions) {
                # Only rewrite exact embedded type names in NUnit's own code.
                # This preserves external BCL names and arbitrary test strings.
                if ($type.Namespace.StartsWith('NUnit.') -and
                    $instruction.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Ldstr -and
                    $renames.ContainsKey([string]$instruction.Operand)) {
                    $instruction.Operand = '_Stripped.' + [string]$instruction.Operand
                    $script:literalCount++
                }
                # net472 defines this compiler attribute in mscorlib. NUnit's facade-qualified
                # lookup can return null without System.Runtime loaded, breaking async-void rejection.
                if ($type.FullName -eq 'NUnit.Framework.Internal.AsyncToSyncAdapter' -and
                    $instruction.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Ldstr -and
                    $instruction.Operand -eq 'System.Runtime.CompilerServices.AsyncStateMachineAttribute, System.Runtime') {
                    $instruction.Operand = 'System.Runtime.CompilerServices.AsyncStateMachineAttribute, mscorlib'
                    $script:literalCount++
                }
            }
        }
    }
    foreach ($nested in $type.NestedTypes) { Repair-TypeDefinition $nested }
}

Repair-Attributes $assembly
Repair-Attributes $module
foreach ($type in $module.Types) { Repair-TypeDefinition $type }
foreach ($reference in $module.GetTypeReferences()) { Repair-TypeReference $reference }
foreach ($type in $module.Types) {
    if ($renames.ContainsKey($type.FullName)) { $type.Namespace = $renames[$type.FullName] }
}

$outputDirectory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputPath))
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
try { $assembly.Write($OutputPath) } finally { $assembly.Dispose() }
Write-Output "Renamed $($renames.Count) types; repaired $script:literalCount NUnit type-name literals and embedded attribute type references."
Write-Output "Restored $indexerCount overloaded indexer property record(s)."
