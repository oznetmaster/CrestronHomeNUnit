// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using NUnit.Framework;

internal static class Program
{
    private static int Main()
    {
        byte[] first = new byte[4096];
        byte[] second = new byte[4096];
        second[100] = 1;
        using (var expected = new MemoryStream(first))
        using (var actual = new MemoryStream(second))
            Console.WriteLine("Unequal streams compare equal: " + Is.EqualTo(expected).ApplyTo(actual).IsSuccess);

        using (var expected = new MemoryStream(new byte[] { 1, 2, 3 }))
        using (var actual = new MemoryStream(new byte[] { 1, 2, 3 }))
        {
            bool equal = Is.EqualTo(expected).ApplyTo(actual).IsSuccess;
            Console.WriteLine("Identical three-byte streams compare equal: " + equal);
            return equal ? 0 : 1;
        }
    }
}
