///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Rsdt.cs
//
//  Note:
//    Page 93 of ACPI 3.0 Spec.

namespace Microsoft.Singularity.Hal.Acpi
{
    using System;
    using Microsoft.Singularity.Io;
    public class Rsdt
    {
        private IoMemory          region;
        private SystemTableHeader header;

        public Rsdt(IoMemory region, SystemTableHeader header)
        {
            this.region = region;
            this.header = header;
        }

        public int EntryCount { get { return region.Length / 4; } }

        public uint GetEntry(int index)
        {
            index *= 4;
            if (index > region.Length)
                return 0;
            return region.Read32(index);
        }

        public SystemTableHeader GetTableHeader(int index)
        {
            uint address = GetEntry(index);
            if (address == 0)
                return null;
            return SystemTableHeader.Create(address);
        }

        public static Rsdt Create(SystemTableHeader header)
        {
            return new Rsdt(
                IoMemory.MapPhysicalMemory(
                    header.PostHeaderAddress, header.PostHeaderLength,
                    true, false
                    ),
                header
                );
        }
    }
}
