// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

// ILRepack keeps the primary assembly attributes. Preserve the upstream test assembly's setting.
[assembly: NUnit.Framework.Parallelizable (NUnit.Framework.ParallelScope.Fixtures)]