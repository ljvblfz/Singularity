///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   AcpiTables.cs
//
//  Note:
//    Based on ACPI 3.0 Spec.

namespace Microsoft.Singularity.Hal.Acpi
{
    using System;
    using Microsoft.Singularity.Io;

    [ CLSCompliant(false) ]
    internal class AcpiTables
    {
        private static Fadt fadt;
        private static Madt madt;

        private static Dsdt dsdt;
        private static Ssdt ssdt;

        private static Srat srat;

        private static PMTimer pmTimer;

        private static UIntPtr GetRsdpBase()
        {
            unsafe
            {
                return BootInfo.GetBootInfo().AcpiRoot32;
            }
        }

        public static Fadt GetFadt()
        {
            return fadt;
        }

        public static Madt GetMadt()
        {
            return madt;
        }

        public static Srat GetSrat()
        {
            return srat;
        }

        public static PMTimer GetPMTimer()
        {
            return pmTimer;
        }

        public static void Parse()
        {
            UIntPtr rsdpBase = GetRsdpBase();

            if (rsdpBase == UIntPtr.Zero) {
                DebugStub.Print("ACPI RSDP not found\n");
            }

            DebugStub.Print("ACPI RSDP address is {0:x8}\n",
                            __arglist(rsdpBase));
            Rsdp rsdp = Rsdp.Parse(rsdpBase, 36u);

            DebugStub.Print("ACPI RSDP OemId is {0:x8}\n",
                            __arglist(rsdp.OemId));
            DebugStub.Print("ACPI RSDP revision is {0:x8}\n",
                            __arglist(rsdp.Revision));
            DebugStub.Print("ACPI RSDT address is {0:x8}\n",
                            __arglist(rsdp.RsdtAddress));

            SystemTableHeader rsdtHeader =
                SystemTableHeader.Create(rsdp.RsdtAddress);

            Rsdt rsdt = Rsdt.Create(rsdtHeader);


            DebugStub.Print("RSDT contains:\n");
            for (int i = 0; i < rsdt.EntryCount; i++) {
                SystemTableHeader header = rsdt.GetTableHeader(i);
                DebugStub.Print("    {0:x8}\n", __arglist(header.Signature));
                if (header.Signature == Fadt.Signature) {
                    fadt = Fadt.Create(header);
                }
                else if (header.Signature == Madt.Signature) {
                    madt = Madt.Create(header);
                }
                else if (header.Signature == Ssdt.Signature) {
                    ssdt = Ssdt.Create(header);
                }
                // haryadi - Srat, Slit
                else if (header.Signature == Srat.Signature)
                {
                    srat = Srat.Create(header);
                    srat.ParseSratStructure();

                    // srat.DumpSratOffsets();
                    // srat.DumpSratImportantFields();
                    // srat.DumpSratStructure();

                }
            }

            if (fadt != null) {
                pmTimer = PMTimer.Create(fadt);
                DebugStub.Print("PMTimer Value={0} Width={1}\n",
                                __arglist(pmTimer.Value, pmTimer.Width));
                uint t0 = pmTimer.Value;
                uint t1 = pmTimer.Value;
                uint t2 = pmTimer.Value;
                uint delta = (t2 >= t1) ? t2 - t1 : ((t1 | 0xff000000) - t2);
                DebugStub.Print("Read cost {0} ticks\n", __arglist(delta));

                if (fadt.DSDT != 0)
                {
                    dsdt = Dsdt.Create(
                        SystemTableHeader.Create(fadt.DSDT)
                        );
                }
            }
            if (dsdt != null) {
                DumpRegion("DSDT", dsdt.Region);
            }
            if (ssdt != null) {
                DumpRegion("SSDT", ssdt.Region);
            }
        }

        private static char Hex(int v)
        {
            if (v < 10) {
                return (char)('0' + v);
            }
            else {
                return (char)('A' + v - 10);
            }
        }

        private static void DumpRegion(string name, IoMemory region)
        {
#if FALSE
            //            (new NamespaceWalker(region)).Display();
            DebugStub.Print("-----------------------------------------------------------------------------\n");
            DebugStub.Print("Table {0} dump\n", __arglist(name));
            DebugStub.Print("-----------------------------------------------------------------------------\n");

            const int step = 16;

            byte last = region.Read8(0);
            for (int i = 0; i < region.Length - 8; i ++) {
                if (region.Read8(i + 0) == (byte)'_' &&
                    (region.Read8(i + 1) == (byte)'C' ||
                     region.Read8(i + 1) == (byte)'H') &&
                    region.Read8(i + 2) == (byte)'I' &&
                    region.Read8(i + 3) == (byte)'D') {

                    int o = 1;
                    byte d0 = region.Read8(i + 4 + o);
                    byte d1 = region.Read8(i + 5 + o);
                    byte d2 = region.Read8(i + 6 + o);
                    byte d3 = region.Read8(i + 7 + o);
                    char c0 = (char)(((d0 & 0x7f) >> 2) + 0x40);
                    char c1 = (char)((((d0 & 0x3) << 3) | (d1 >> 5)) + 0x40);
                    char c2 = (char)((d1 & 0x1f) + 0x40);
                    char c3 = Hex(d2 >> 4);
                    char c4 = Hex(d2 & 0xf);
                    char c5 = Hex(d3 >> 4);
                    char c6 = Hex(d3 & 0xf);
                    DebugStub.Print("{0:x8} -> _{8}ID {1}{2}{3}{4}{5}{6}{7}\n",
                                    __arglist(i, c0, c1, c2,
                                              c3, c4, c5, c6,
                                              (char)region.Read8(i + 1)));
                }
            }

            for (int i = 0; i < region.Length; i += step) {
                DebugStub.Print("{0:x8} : ", __arglist(i));
                int n = step;
                if (region.Length - i < n) {
                    n = region.Length - i;
                }
                for (int j = 0; j < n; j++) {
                    char c = (char)region.Read8(i + j);
                    DebugStub.Print("{0:x2} ", __arglist((int)c));
                }
                for (int j = 0; j < n; j++) {
                    char c = (char)region.Read8(i + j);
                    if (c < 32 || c > 127)
                        c = '.';
                    DebugStub.Print(c.ToString());
                }
                DebugStub.Print("\n");
            }
            DebugStub.Print("\n");
#endif // FALSE
        }

        internal class NamespaceWalker
        {
            int cursor = 0;
            IoMemory memory;

            internal NamespaceWalker(IoMemory m)
            {
                this.memory = m;
            }

            byte Read8()
            {
                return this.memory.Read8(cursor++);
            }

            ushort Read16()
            {
                ushort r = this.memory.Read16(cursor);
                cursor += 2;
                return r;
            }

            uint Read32()
            {
                uint r = this.memory.Read32(cursor++);
                cursor += 4;
                return r;
            }

            int GetPackageLength()
            {
                byte b = Read8();
                int length    = (int)b & 0x3f;
                int following = ((int)b & 0xc) >> 6;
                while (following-- > 0) {
                    length = length << 8;
                    length += (int)Read8();
                }
                return length;
            }

            void Reset()
            {
                cursor = 0;
            }

            internal void Display()
            {
                while (cursor < this.memory.Length) {
                    DisplayPackage(0);
                }
            }

            void DisplayPackage(int depth)
            {
                int length    = GetPackageLength();
                int oldCursor = this.cursor;
                DebugStub.Print("Package Length {0} Name {1}{2}{3}{4}",
                                __arglist(length,
                                          (char)Read8(),
                                          (char)Read8(),
                                          (char)Read8(),
                                          (char)Read8()));
                cursor = oldCursor + length;
            }
        }
    }
}
