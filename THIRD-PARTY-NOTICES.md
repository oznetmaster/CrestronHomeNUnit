# Third-party notices

Crestron Home NUnit includes or depends on the following third-party software. Copyright and license terms for these components are independent of the root project license. Merging assemblies or renaming compatibility types does not remove those terms.

This inventory describes the dependencies restored for the initial publication draft. Keep it synchronized with dependency upgrades and the actual contents of release assets. Some support packages supply source or reference-only assets rather than standalone runtime DLLs.

## Application icon

The official runner release uses GlyphLab Stock Icons 2015 v1.4, **Code – Play**, Copyright York Technologies Limited, under the supplied commercial icon license. Its 16, 32 and 64 pixel images are embedded in the application. The original icon files are not distributed as source or standalone release assets and are not licensed under this project's MIT license. The license text is retained in [licenses/GlyphLab-LICENSE.txt](licenses/GlyphLab-LICENSE.txt).

Public source builds use the original MIT-licensed test-grid/checkmark icon in `CrestronHomeNUnit.Runner/Assets`. A licensed maintainer can provide a private `RunnerIconPath` MSBuild property. Official CI receives the licensed icon through the private `RUNNER_ICON_BASE64` secret; no icon data or workstation path is committed.

## NUnit framework and selected framework self-tests

- Project: [NUnit](https://github.com/nunit/nunit).
- Framework package: **NUnit 4.6.1**, obtained from NuGet; the framework is not compiled from a private source fork.
- Imported self-test source: tag **v4.6.1**, commit **b9197a6f17635580a3a397f3eb0f28bddba2e0c7**.
- Upstream file attribution: **Copyright (c) Charlie Poole, Rob Prouse and Contributors**.
- Pinned license attribution: **Copyright (c) 2024 Charlie Poole, Rob Prouse**.
- License: MIT; full unchanged text in [licenses/NUnit-4.6.1-LICENSE.txt](licenses/NUnit-4.6.1-LICENSE.txt), also retained in `vendor/nunit/LICENSE.txt` in the source repository.

The selected Assertions, Constraints and Syntax tests, their supporting utilities, test-data fixtures and the upstream test signing key remain NUnit material. The key is the public test key supplied by the NUnit repository to support friend-assembly access; it is not a private release/deployment credential.

Local source adaptations are recorded in `vendor/nunit/PROVENANCE.md`: driver-work-directory test files and diagnostics, recreation of a disposed wait event between runs, and fresh mutable constraint data for repeated discovery. Local modification notices identify Neil Colvin's changes while retaining the upstream notice and MIT license. A notice-only header was added to `ConstraintCastingTests.cs`, which had no existing copyright header. No claim is made that the selected tests are the entire NUnit test suite.

Post-merge modifications to the framework include compatibility namespace/type-reference repairs, the net472 async-state-machine reflection lookup, and restoration of metadata affected by ILRepack. These adaptations are described in the README and packaging scripts.

The separately copied `System.Threading.Lock` compatibility source retains its NUnit notice and the accompanying [NUnit shim license](licenses/NUnit-Shims-LICENSE.txt), whose attribution differs from the pinned v4.6.1 license. `OverloadResolutionPriorityAttribute.cs` retains the .NET Foundation's MIT notice; see [the .NET license](licenses/DotNet-LICENSE.txt).

## Configuration workflow dependency

The CLI workflow and Test Explorer adapter use [CrestronHomeDevTools 1.1.0](https://github.com/oznetmaster/CrestronHomeDevTools), copyright (c) 2026 Neil Colvin, under the MIT license. It uses the same SSH.NET version listed below and adds no Crestron SDK runtime dependency to the desktop tooling. The adapter's NuGet package includes project-owned Workflow, Client and Transport assemblies under the root MIT license; third-party runtime dependencies are restored as separate NuGet packages with their own licenses. Microsoft.TestPlatform.ObjectModel 18.9.0 is a build dependency under Microsoft's MIT license, supplied at runtime by the VSTest host.

## Runtime and source dependencies

| Component and restored version | Attribution | License text |
| --- | --- | --- |
| Makaretu.Dns 2.0.1 | Richard Schneider; package metadata © 2018–2019, upstream license © 2018 | [MIT](licenses/Makaretu.Dns-LICENSE.txt) |
| Makaretu.Dns.Multicast 0.27.0 | Richard Schneider; package metadata © 2018–2019, upstream license © 2018 | [MIT](licenses/Makaretu.Dns.Multicast-LICENSE.txt) |
| Common.Logging and Common.Logging.Core 3.4.1 | Aleksandar Seovic, Mark Pollack, Erich Eichinger, Stephen Bohlen and contributors | [Apache-2.0](licenses/Common.Logging-LICENSE.txt) |
| IPNetwork2 2.1.2 (`System.Net.IPNetwork.dll`) | Luc Dvchosal / lduchosal; upstream license © 2015 | [BSD-2-Clause](licenses/IPNetwork2-LICENSE.txt) |
| SimpleBase 1.3.1 | Copyright 2014–2017 Sedat Kapanoglu | [Apache-2.0](licenses/SimpleBase-LICENSE.txt) |
| Tmds.LibC 0.2.0 (transitive runner dependency) | Tom Deseyn | [MIT](licenses/Tmds.LibC-LICENSE.txt) |
| SSH.NET 2026.0.0 | Copyright © Renci 2010–2026; additional authors retained in the upstream license | [MIT](licenses/SSH.NET-LICENSE.txt) |
| BouncyCastle.Cryptography 2.7.0 | Copyright (c) 2000–2026 The Legion of the Bouncy Castle Inc. | [MIT](licenses/BouncyCastle-LICENSE.md) |
| Hafner.Compatibility.MetaPackage 1.9.0 and its source-compatibility dependencies | Christoph Hafner | [MIT](licenses/Hafner.Compatibility-LICENSE.txt) |
| IsExternalInit 1.0.3, supplying build-time compatibility source | Manuel Römer | [MIT](licenses/IsExternalInit-LICENSE.txt) |
| Microsoft/.NET support packages listed below | .NET Foundation and Contributors / Microsoft; individual package notices also apply | [MIT](licenses/DotNet-LICENSE.txt) |

Hafner packages restored by the processor project: CallerInformationAttributes.G2 1.0.0; ConstantExpectedAttribute 2.0.0; DynamicallyAccessedMembersAttribute 1.1.3; ExperimentalAttribute 1.0.0; FeatureGuardAttribute 1.0.0; NullableReferenceTypeAttributes.G1, G2 and G3 1.0.3. The metapackage itself is 1.9.0. Original source notices in NuGet-generated compatibility files remain intact.

Microsoft/.NET support packages restored across the runner and processor graphs include Microsoft.Bcl.AsyncInterfaces 8.0.0 and 10.0.12, Microsoft.Bcl.Cryptography 10.0.10, Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2, Microsoft.Extensions.Logging.Abstractions 8.0.3, System.Buffers 4.6.1, System.Collections.Immutable 8.0.0, System.Formats.Asn1 10.0.10, System.Memory 4.6.3, System.Numerics.Vectors 4.6.1, System.Runtime.CompilerServices.Unsafe 6.1.2, System.Threading.Tasks.Extensions 4.5.4 and 4.6.3, and System.ValueTuple 4.4.0 and 4.6.2. Framework reference dependencies may be satisfied by installed framework assemblies rather than copied into a distribution.

Project and source links:

- [Makaretu DNS](https://github.com/richardschneider/net-dns), [multicast DNS](https://github.com/richardschneider/net-mdns).
- [Common.Logging](https://github.com/net-commons/common-logging), [IPNetwork](https://github.com/lduchosal/ipnetwork), [SimpleBase](https://github.com/ssg/SimpleBase).
- [SSH.NET](https://github.com/sshnet/SSH.NET), [Bouncy Castle](https://github.com/bcgit/bc-csharp).
- [Hafner compatibility metapackage](https://github.com/HugoRoss/Hafner.Compatibility.MetaPackage), [IsExternalInit](https://github.com/manuelroemer/IsExternalInit), [.NET runtime](https://github.com/dotnet/runtime).

## License-text provenance

NUnit 4.6.1 and the copied NUnit shim licenses come from the existing source imports. Bouncy Castle's license comes from its restored 2.7.0 NuGet package. Makaretu.Dns is pinned to package repository commit `701463d2091e6d98d4cc4490abb0e0ead8ae2985`; SSH.NET to `7b2fd3dbf2c86a80a7b06cea020aa5f821c9902e`; Hafner's metapackage to `e08289a2da4abfaac9b739a7987a74f6daf0622f`.

The remaining license texts were obtained from their upstream repositories for this publication review. SimpleBase 1.3.1's NuGet metadata identifies the Apache license; the included Apache-2.0 text was obtained from upstream tag 1.7.0 because no 1.3.1 tag was available in the upstream tag listing. This does not change the shipped dependency version. The .NET MIT text is from runtime tag v10.0.0. `licenses/SOURCES.json` records retrieval URLs where applicable; local package/import sources are described here.

## Bundled .NET runtime

The self-contained Windows runner includes Microsoft.NETCore.App and Microsoft.WindowsDesktop.App 10.0.12. `PublishRunner.ps1` copies the exact runtime packs' license files into `licenses/bundled-runtime`, including .NET runtime `THIRD-PARTY-NOTICES.TXT`. These additional notices cover components bundled by Microsoft and must remain in binary redistributions. They are obtained from the restored runtime packs for the selected version and architecture, rather than substituted with a general MIT notice.

Tmds.LibC 0.2.0 is restored through SSH.NET's modern .NET dependency graph; its full MIT notice comes from the [upstream repository](https://github.com/tmds/Tmds.LibC/blob/master/LICENSE).

## Crestron SDK and build tools

Crestron.DeviceDrivers.DevKit 27.0.24, Crestron.DeviceDrivers.ProgramInterface 27.0.24 and Crestron.SimplSharp.SDK.Library 2.21.90 are proprietary SDK/platform dependencies, governed by their respective Crestron license agreements. They are not relicensed under MIT. The packaging targets keep Crestron assemblies as platform references instead of merging them into the test host. Obtain the SDK and ManifestUtil separately under Crestron's terms.

The .NET SDK, Visual Studio/MSBuild, PowerShell, ILRepack and Posh-SSH are separately installed build/deployment tools. This repository does not bundle their installers. The ILRepack metadata repair/reproduction tools use an installed Mono.Cecil supplied with ILRepack; that build tool DLL is not included in the Windows runner ZIP.

## Redistribution

Include the root license, this notice and the applicable full third-party license texts when redistributing binary releases. Processor packages carry the host notices under `Licenses/CrestronHomeNUnit`; Windows runner distributions carry them beside the executable. Keep upstream source notices when distributing source.

Authors of additional test packages must also include the licenses for their own tests and application dependencies. This notice covers the shared host and this repository's supplied components; it does not automatically cover every third-party library a future test suite may reference.
