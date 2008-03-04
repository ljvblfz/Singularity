///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   SmapInfo.sg
//
//  Note:
//       Section 14 System Address Map Interfaces,
//       ACPI revision 3.0, September 2, 2004

using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Microsoft.Singularity
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct SMAPINFO
    {
        [AccessedByRuntime("referenced from c++")]
        internal const uint AddressTypeFree     = 1;
        [AccessedByRuntime("referenced from c++")]
        internal const uint AddressTypeReserved = 2;
        [AccessedByRuntime("referenced from c++")]
        internal const uint AddressTypeACPI     = 3;
        [AccessedByRuntime("referenced from c++")]
        internal const uint AddressTypeNVS      = 4;
        [AccessedByRuntime("referenced from c++")]
        internal const uint AddressTypeUnusable = 5;
        [AccessedByRuntime("referenced from c++")]
        internal const uint AddressTypeMax      = 5;

        [AccessedByRuntime("referenced from c++")]
        internal const uint ExtendedAttributeRangeEnabled = 1;
        [AccessedByRuntime("referenced from c++")]
        internal const uint ExtendedAttributeRangeNV      = 2;

        [AccessedByRuntime("referenced from c++")]
        internal ulong      addr;
        [AccessedByRuntime("referenced from c++")]
        internal ulong      size;
        [AccessedByRuntime("referenced from c++")]
        internal uint       type;
        [AccessedByRuntime("referenced from c++")]
        internal uint       extendedAttributes;

        internal enum AddressType : uint
        {
            Free     = AddressTypeFree,
            Reserved = AddressTypeReserved,
            ACPI     = AddressTypeACPI,
            NVS      = AddressTypeNVS,
            Unusable = AddressTypeUnusable,
            Max      = AddressTypeMax
        }
    }
}
